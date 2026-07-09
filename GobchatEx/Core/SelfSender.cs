using System;

namespace GobchatEx.Core;

/// <summary>
/// String-level own-message heuristic, shared by the native listener (sound suppression,
/// worldless-sender completion) and the Chat 2 style provider (identity completion in its
/// Evaluate path) so the two can't drift: a self channel (TellOutgoing/Echo — the caller maps
/// the channel, since XivChatType is Dalamud-level) is unconditionally self; otherwise the
/// sender text must contain the local player's name — ordinal, tolerant of party-number
/// prefixes and cross-world suffixes. A missing local name (not logged in) is never self.
/// </summary>
public static class SelfSender
{
    public static bool IsSelf(bool selfChannel, string senderText, string? localName)
        => selfChannel
            || (!string.IsNullOrEmpty(localName)
                && senderText.Contains(localName, StringComparison.Ordinal));
}
