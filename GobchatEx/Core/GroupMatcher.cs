using System.Collections.Generic;

namespace GobchatEx.Core;

/// <summary>
/// One player added to a custom group's member list. <see cref="World"/> is null for a bare, cross-
/// world-agnostic entry (matches this player name on any world); stored and displayed with whatever
/// casing the player/UI originally provided — folding only ever happens at match time, never at rest.
/// </summary>
public sealed record GroupMember(string Player, string? World);

/// <summary>
/// One player-group rule: either a custom group matched against <see cref="Members"/>, or a game
/// friend-group matched by <see cref="FfGroup"/> (the sender's <c>DisplayGroup</c> index, 0-6).
/// </summary>
public sealed record GroupRule(string Id, bool Active, int? FfGroup, IReadOnlyList<GroupMember> Members);

/// <summary>
/// Tags a chat message's sender with a group id, ported from the app's
/// ChatMessageTriggerGroupSetter/TriggerGroup. Member names are matched case-insensitively against an
/// NFKC-folded copy of the sender name (so decorative "fancy font" senders still match a plain-text
/// entry); a member with no world matches that name on any world, a member with a world only matches
/// when the sender's world also matches — a member added on any world still matches on every world via
/// the bare (no-world) form. First active match in <paramref name="orderedGroups"/> wins; callers order
/// the list (customs before friend groups) so customs take precedence.
/// </summary>
public static class GroupMatcher
{
    public static string? FindGroup(
        string senderName,
        string? senderWorld,
        int? friendGroupIndex,
        IReadOnlyList<GroupRule> orderedGroups)
    {
        var bareName = Fold(senderName);

        foreach (var group in orderedGroups)
        {
            if (!group.Active)
                continue;

            if (group.FfGroup.HasValue)
            {
                if (group.FfGroup.Value == friendGroupIndex)
                    return group.Id;
                continue;
            }

            if (MatchesMembers(group.Members, bareName, senderWorld))
                return group.Id;
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="member"/> covers the player (<paramref name="playerName"/>,
    /// <paramref name="playerWorld"/>) under the same rules chat matching uses: NFKC-folded
    /// case-insensitive names, a bare (no-world) member matching the name on any world, and a
    /// world-qualified member requiring a known, equal world. The membership UIs (context menus,
    /// /gobchat group, settings tab) share this one predicate so their add/remove wording always
    /// agrees with what <see cref="FindGroup"/> would actually color.
    /// </summary>
    public static bool IsMember(GroupMember member, string playerName, string? playerWorld)
        => MatchesMember(member, Fold(playerName), playerWorld);

    private static bool MatchesMembers(IReadOnlyList<GroupMember> members, string bareName, string? senderWorld)
    {
        foreach (var member in members)
        {
            if (MatchesMember(member, bareName, senderWorld))
                return true;
        }

        return false;
    }

    private static bool MatchesMember(GroupMember member, string foldedName, string? world)
    {
        if (Fold(member.Player) != foldedName)
            return false;

        if (string.IsNullOrEmpty(member.World))
            return true; // bare entry: matches this name on any world

        return world != null && Fold(member.World) == Fold(world);
    }

    private static string Fold(string value) => UnicodeNormalizer.Normalize(value).ToLowerInvariant();
}
