using System;
using System.Collections.Generic;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace GobchatEx.Config;

/// <summary>
/// Chat logging settings (Milestone 5), persisted to chatlog.json: write chat to per-session .log
/// files, one file per login/character switch. Whether logging is running is deliberately NOT
/// config — it is a session-scoped action on the ChatLogger (start/stop button, or the opt-in
/// <see cref="AutoStartLogging"/> at login; always off after logout) — only the folder, format,
/// channel selection, and the auto-start opt-in live here. The line format is fixed to the app's default; it stays hand-editable here (unknown
/// tokens render literally) but has no settings UI yet.
/// </summary>
[Serializable]
public class ChatLogConfig
{
    public const string DefaultLogFormat = "{channel} [{date} {time-full}] {sender}: {message}";

    public int Version { get; set; } = 1;

    /// <summary>Log output folder. Empty = not configured — there is no default, and logging
    /// cannot start until the user picks a folder. The folder picker stores absolute paths; a
    /// hand-edited relative path resolves inside the config directory (PathSecurityUtil) and
    /// counts as unusable when it would escape it.</summary>
    public string LogFolder { get; set; } = string.Empty;

    /// <summary>Write each character's logs into their own subfolder under the log folder.</summary>
    public bool UseCharacterFolders { get; set; } = true;

    /// <summary>Start logging automatically on every character login. Opt-in (off by default) so
    /// users who already picked a folder never start writing chat to disk unasked; has no effect
    /// while no usable folder is configured.</summary>
    public bool AutoStartLogging { get; set; }

    public string LogFormat { get; set; } = DefaultLogFormat;

    /// <summary>The app logged every channel it saw; here the equivalent is every conversational
    /// channel, plus Echo (handy for testing and for plugin output worth archiving).</summary>
    public static readonly XivChatType[] DefaultLogChannels =
    [
        XivChatType.Say,
        XivChatType.CustomEmote,
        XivChatType.StandardEmote,
        XivChatType.Yell,
        XivChatType.Shout,
        XivChatType.Party,
        XivChatType.CrossParty,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.TellIncoming,
        XivChatType.TellOutgoing,
        XivChatType.NoviceNetwork,
        XivChatType.Echo,
        XivChatType.Ls1,
        XivChatType.Ls2,
        XivChatType.Ls3,
        XivChatType.Ls4,
        XivChatType.Ls5,
        XivChatType.Ls6,
        XivChatType.Ls7,
        XivChatType.Ls8,
        XivChatType.CrossLinkShell1,
        XivChatType.CrossLinkShell2,
        XivChatType.CrossLinkShell3,
        XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5,
        XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7,
        XivChatType.CrossLinkShell8,
    ];

    // Replace, not Reuse: same Json.NET default-list-append bug as FormattingConfig.HighlightChannels.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<XivChatType> LogChannels { get; set; } = [.. DefaultLogChannels];
}
