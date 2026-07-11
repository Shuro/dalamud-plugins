using System;
using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Opt-in fallback for the pre-Dalamud standalone app's "/e gc ..." command palette — that app could
/// only observe echoed chat text (no real slash-command registration), so old macros still send
/// commands as "/e gc &lt;command&gt;" (FFXIV's "/echo" alias). Gated by
/// <see cref="Config.GeneralConfig.LegacyEchoCommandFallback"/>, checked fresh on every message (no
/// caching needed — <see cref="Config.Configuration.General"/> is a stable, in-place-mutated
/// reference per <see cref="Config.Configuration"/>'s own documented convention). The "gc " prefix
/// match itself lives in <see cref="LegacyEchoCommand"/> (Dalamud-free, unit tested).
/// </summary>
internal sealed class LegacyCommandListener : IDisposable
{
    private readonly Plugin plugin;

    public LegacyCommandListener(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.ChatGui.CheckMessageHandled += OnChatMessage;
    }

    public void Dispose() => Plugin.ChatGui.CheckMessageHandled -= OnChatMessage;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!plugin.Configuration.General.LegacyEchoCommandFallback)
            return;

        // Echo is inherently local-only — it can never originate from another player (same reasoning
        // as ChatListener's IsSelfChannel) — so this only ever reacts to the user's own locally
        // generated "/echo" text, never to anything sent by anyone else.
        if (message.LogKind != XivChatType.Echo)
            return;

        var rest = LegacyEchoCommand.TryMatch(BuildCommandText(message.Message));
        if (rest == null)
            return;

        try
        {
            CommandDispatcher.Execute(plugin, rest);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Legacy \"/e gc\" command failed");
        }
    }

    /// <summary>
    /// Reconstructs command text from the message's payloads instead of using SeString.TextValue
    /// directly. A macro-substituted "&lt;t&gt;" for a cross-world target renders as a name text run,
    /// a CrossWorld icon payload (no text of its own), then a world text run — TextValue silently
    /// drops the icon payload, concatenating name and world with no separator at all (e.g. "Lancefer
    /// ChastainZodiark"). This renders that icon boundary as this codebase's own "Name [World]"
    /// bracket convention instead, so the existing group/player command parsers — which already expect
    /// that bracket format — keep working unmodified. Mirrors <see cref="SenderIdentity"/>'s
    /// PlayerPayload-first / icon-fallback approach, adapted for a full multi-token message rather
    /// than a sender-only SeString (so the world-name run has to stop at the first following
    /// whitespace, not run to the end of the message).
    /// </summary>
    private static string BuildCommandText(SeString message)
    {
        var sb = new StringBuilder();
        var inWorldRun = false;

        foreach (var payload in message.Payloads)
        {
            switch (payload)
            {
                case PlayerPayload player:
                    var world = player.World.ValueNullable?.Name.ExtractText();
                    sb.Append(GroupMembershipActions.FormatPlayer(player.PlayerName, world));
                    break;

                case IconPayload { Icon: BitmapFontIcon.CrossWorld }:
                    inWorldRun = true;
                    sb.Append(" [");
                    break;

                case TextPayload { Text.Length: > 0 } textPayload when inWorldRun:
                    var text = textPayload.Text;
                    var boundary = text.IndexOf(' ');
                    var worldName = boundary < 0 ? text : text[..boundary];
                    var remainder = boundary < 0 ? string.Empty : text[boundary..];
                    sb.Append(worldName).Append(']').Append(remainder);
                    inWorldRun = false;
                    break;

                case TextPayload { Text.Length: > 0 } textPayload:
                    sb.Append(textPayload.Text);
                    break;
            }
        }

        return sb.ToString();
    }
}
