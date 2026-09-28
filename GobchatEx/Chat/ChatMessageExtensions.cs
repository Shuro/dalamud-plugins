using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Lumina.Text.ReadOnly;

namespace GobchatEx.Chat;

internal static class ChatMessageExtensions
{
    /// <summary>The message's game timestamp in local time; now when the game supplied none.</summary>
    public static DateTimeOffset LocalTimestamp(this IHandleableChatMessage message)
        => message.Timestamp > 0
            ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp).ToLocalTime()
            : DateTimeOffset.Now;

    /// <summary>Re-encodes a Dalamud SeString as Lumina's read-only form for payload-level iteration.</summary>
    public static ReadOnlySeString ToRoss(this SeString value) => new(value.Encode());
}
