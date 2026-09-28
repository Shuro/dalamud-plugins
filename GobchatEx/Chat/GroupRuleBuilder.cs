using System.Collections.Generic;
using GobchatEx.Config;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Single source of the group-rule precedence invariant, shared by the native pass
/// (<see cref="ChatListener"/>) and the Chat 2 provider (<see cref="ChatTwoStyleProvider"/>):
/// custom groups first, in config order (GroupMatcher's first-match-wins gives them precedence
/// over friend groups on the same sender), then the 7 friend groups in FfGroup order
/// (Configuration.Load normalizes a hand-edited groups.json, and nothing reorders them
/// afterward). Style dictionaries stay caller-built — the native pass styles
/// foreground/glow, the provider Chat 2 backgrounds — and they're keyed by id, so only the
/// rule list carries the ordering.
/// </summary>
internal static class GroupRuleBuilder
{
    /// <summary>
    /// <paramref name="snapshotMembers"/>: the provider copies each member list because its
    /// Evaluate path reads the rules on Chat 2's processing thread while GroupMembershipActions
    /// mutates the live config lists on the framework thread; the native pass reads the live
    /// lists on the framework thread only and skips the copy.
    /// </summary>
    public static List<GroupRule> Build(GroupsConfig groups, bool snapshotMembers)
    {
        var rules = new List<GroupRule>(groups.Groups.Count + groups.FriendGroups.Count);

        foreach (var group in groups.Groups)
        {
            IReadOnlyList<GroupMember> members = snapshotMembers ? [.. group.Members] : group.Members;
            rules.Add(new GroupRule(group.Id, group.Active, FfGroup: null, members));
        }

        foreach (var group in groups.FriendGroups)
            rules.Add(new GroupRule(group.Id, group.Active, group.FfGroup, Members: []));

        return rules;
    }
}
