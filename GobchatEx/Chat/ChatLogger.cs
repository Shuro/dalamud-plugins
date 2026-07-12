using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using GobchatEx.Config;
using GobchatEx.Core;
using GobchatEx.Core.Util;

namespace GobchatEx.Chat;

/// <summary>
/// Writes chat to per-session .log files (Milestone 5). Deliberately its own
/// CheckMessageHandled subscriber rather than a hook inside ChatListener: it reads
/// OriginalSender/OriginalMessage — which no plugin can mutate, so multicast ordering is
/// irrelevant to log content — and a logging failure must never break highlighting (or vice
/// versa). Messages suppressed by another plugin (IsHandled) are skipped: the log mirrors what
/// the player actually sees. Session rules (lazy file creation, one file per login/character
/// switch, drop while logged out) live in the testable <see cref="ChatLogSession"/>; this class
/// only maps Dalamud types and appends to disk, batched via Framework.Update so a busy channel
/// costs one file open per second, not per line. Logging is a session-scoped manual action:
/// it never starts by itself, is forced off at logout, cannot start without a user-chosen log
/// folder (there is no default), and the on/off state is not persisted
/// (<see cref="StartLogging"/>/<see cref="StopLogging"/>). Chat events, Framework.Update,
/// settings commits, and Dispose all run on the framework thread, so no locking is needed.
/// </summary>
internal sealed class ChatLogger : IDisposable
{
    private const long FlushIntervalMs = 1000;

    private readonly ChatLogConfig _config;
    private readonly ChatLogSession _session = new(() => DateTimeOffset.Now);

    private ChatLogFormatter _formatter = null!;
    private HashSet<XivChatType> _channels = null!;
    private long _nextFlush;
    private bool _ioErrorLogged;
    private bool _chatMessageErrorLogged;

    /// <summary>The folder log files are written to, after relative resolution; empty while no
    /// usable folder is configured — the settings tab displays it under the folder input.</summary>
    internal string ResolvedLogFolder { get; private set; } = string.Empty;

    /// <summary>True when a configured folder was unusable (escaped the config directory or
    /// was not a valid path); logging is disabled until it is fixed.</summary>
    internal bool LogFolderInvalid { get; private set; }

    /// <summary>Whether a usable log folder is configured — the precondition for
    /// <see cref="StartLogging"/>; the start buttons are disabled while false.</summary>
    internal bool HasLogFolder => ResolvedLogFolder.Length > 0;

    /// <summary>The file the current session is appending to; null until the first line lands.</summary>
    internal string? CurrentFilePath => _session.CurrentFilePath;

    /// <summary>Whether chat is currently being logged. Session-scoped: starts false, flipped
    /// only by <see cref="StartLogging"/>/<see cref="StopLogging"/>, forced off at logout.</summary>
    internal bool IsLogging { get; private set; }

    internal ChatLogger(ChatLogConfig config)
    {
        _config = config;
        SettingsChanged();

        // A mid-session (re)load — plugin update or dev auto-reload — never fires Login, so seed
        // the character now. Plugin construction is only framework-thread when the manifest sets
        // LoadSync (ours doesn't), and IPlayerState throws off-thread, so dispatch.
        if (Plugin.ClientState.IsLoggedIn)
        {
            _ = Plugin.Framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    _session.SetCharacter(Plugin.PlayerState.CharacterName);
                }
                catch (Exception ex)
                {
                    // Fire-and-forget dispatch: without this, a throw would vanish into the
                    // discarded task; the defensive re-seed in OnChatMessage still recovers.
                    Plugin.Log.Error(ex, "Initial chat-log character seed failed; retrying on the next message.");
                }
            });
        }

        Plugin.ChatGui.CheckMessageHandled += OnChatMessage;
        Plugin.ClientState.Login += OnLogin;
        Plugin.ClientState.Logout += OnLogout;
        Plugin.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.ClientState.Logout -= OnLogout;
        Plugin.ClientState.Login -= OnLogin;
        Plugin.ChatGui.CheckMessageHandled -= OnChatMessage;
        FlushNow(); // after unsubscribing, so nothing enqueues mid-teardown
    }

    /// <summary>Starts logging; a no-op while no usable folder is configured (there is no
    /// default — the user must pick one). The session file is still created lazily with the
    /// first message.</summary>
    internal void StartLogging()
    {
        if (HasLogFolder)
            IsLogging = true;
    }

    /// <summary>
    /// Stops logging after flushing what is pending. The session file stays open-ended: starting
    /// again during the same login continues it — rotation stays tied to login/character switch
    /// and folder changes.
    /// </summary>
    internal void StopLogging()
    {
        FlushNow();
        IsLogging = false;
    }

    /// <summary>Call after any configuration change (SettingsWindow's commit).</summary>
    public void SettingsChanged()
    {
        FlushNow(); // pending lines belong to the old folder/format
        _ioErrorLogged = false;
        _chatMessageErrorLogged = false;

        _channels = [.. _config.LogChannels];
        _formatter = new ChatLogFormatter(
            string.IsNullOrWhiteSpace(_config.LogFormat) ? ChatLogConfig.DefaultLogFormat : _config.LogFormat);

        ResolvedLogFolder = ResolveLogFolder(
            _config.LogFolder, Plugin.PluginInterface.ConfigDirectory.FullName, out var invalid);
        if (invalid && !LogFolderInvalid)
            Plugin.Log.Warning(
                "Configured chat-log folder {Folder} is unusable; logging is disabled until it is fixed.",
                _config.LogFolder);
        LogFolderInvalid = invalid;

        if (HasLogFolder)
        {
            _session.Configure(ResolvedLogFolder, _config.UseCharacterFolders);
        }
        else if (IsLogging)
        {
            // The folder was cleared or broke mid-session (the flush above already landed in the
            // old folder). The session deliberately keeps its last folder: with IsLogging false
            // nothing enqueues, so no line can target the stale path.
            IsLogging = false;
            Plugin.Log.Warning("Chat logging stopped: no usable log folder is configured.");
        }
    }

    /// <summary>
    /// Resolves the configured log folder. There is no default: empty means unconfigured and
    /// resolves to an empty string, which keeps logging disabled. The folder picker stores
    /// absolute paths (allowed anywhere); a hand-edited relative path must resolve inside the
    /// config directory (PathSecurityUtil) — escaping or malformed paths are flagged invalid
    /// and also resolve to empty.
    /// </summary>
    internal static string ResolveLogFolder(string configured, string configDir, out bool invalid)
    {
        invalid = false;
        var trimmed = configured.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        try
        {
            // IsPathFullyQualified, not IsPathRooted: drive-relative ("C:logs") and root-relative
            // ("\logs") strings count as rooted but GetFullPath resolves them against the current
            // working directory — they must go through the containment check, not the
            // trusted-picker branch, or a hand-edited config escapes the sandbox.
            return Path.IsPathFullyQualified(trimmed)
                ? Path.GetFullPath(trimmed)
                : PathSecurityUtil.ResolveWithin(configDir, trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
            or UnauthorizedAccessException or System.Security.SecurityException)
        {
            invalid = true;
            return string.Empty;
        }
    }

    private void OnLogin()
    {
        FlushNow();
        _session.SetCharacter(Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.CharacterName : null);
    }

    private void OnLogout(int type, int code)
    {
        FlushNow(); // finalize the session's file before the character goes away
        _session.SetCharacter(null);
        IsLogging = false; // logging is per login session — it never carries across a logout
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!IsLogging || message.IsHandled || !_channels.Contains(message.LogKind))
            return;

        // Login raced ahead of the player data, or the hot-reload seed is still in flight.
        if (_session.CharacterName == null)
        {
            if (!Plugin.PlayerState.IsLoaded)
                return; // logged out: dropped by design
            _session.SetCharacter(Plugin.PlayerState.CharacterName);
            if (_session.CharacterName == null)
                return;
        }

        try
        {
            // The message object is pooled and only valid during this callback — extract plain
            // strings now, keep no reference. Original* is the pre-plugin-edit text: the log
            // archives what the game said, not GobchatEx's recoloring or another plugin's edits.
            var timestamp = message.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp).ToLocalTime()
                : DateTimeOffset.Now;
            // ResolveDisplay keeps the game-rendered sender prefix (friend-group glyph, party
            // number) that Resolve's clean name drops — the log archives what the player sees.
            SenderIdentity.ResolveDisplay(message.OriginalSender.ToDalamudString(), out var name, out var world);
            var sender = world == null ? name : $"{name}@{world}";
            // Emote bodies start with the sender/message join-space (the native line renders
            // sender + message); the format's own ": " join makes it redundant. Only emotes:
            // every other channel's body is archived exactly as the game sent it.
            var body = message.OriginalMessage.ToDalamudString().TextValue;
            if (message.LogKind is XivChatType.CustomEmote or XivChatType.StandardEmote)
                body = body.TrimStart();

            _session.Enqueue(_formatter.Format(new ChatLogEntry(
                timestamp, ChatLogChannelNames.Get(message.LogKind), sender, body)));
        }
        catch (Exception ex)
        {
            // Same policy as ChatListener: never throw on the shared chat pass, log once per
            // settings generation instead of once per message.
            if (_chatMessageErrorLogged)
                return;
            _chatMessageErrorLogged = true;
            Plugin.Log.Error(ex, "Chat logger failed to capture a message; further capture errors are suppressed.");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_session.PendingCount == 0 || Environment.TickCount64 < _nextFlush)
            return;
        _nextFlush = Environment.TickCount64 + FlushIntervalMs;
        FlushNow();
    }

    private void FlushNow()
    {
        if (_session.DequeueWrite() is not { } write)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(write.FilePath)!);
            // The encoding-less overload writes UTF-8 without a BOM — part of the "clean new
            // format" decision (the app emitted a BOM on the first write).
            File.AppendAllLines(write.FilePath, write.Lines);
            if (write.IsNewFile)
                Plugin.Log.Information("Started chat log {Path}.", write.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Log-and-continue (config-save convention): the batch is dropped rather than
            // requeued so a dead disk can't grow the queue without bound; logged once per
            // settings generation.
            if (_ioErrorLogged)
                return;
            _ioErrorLogged = true;
            Plugin.Log.Error(ex, "Failed to write chat log {Path}; dropping batch, further write errors are suppressed.", write.FilePath);
        }
    }
}
