using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GobchatEx.Config;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Shared add/remove/membership-check logic for one player against a custom group's member list, used
/// by the slash command, the native right-click context menu, and the Chat 2 IPC integration alike.
/// Bound to one (name, world) pair at construction (the right-clicked or /gex-group-targeted
/// player); callers iterate <see cref="GroupsConfig.Groups"/> and pass each <see cref="PlayerGroup"/>
/// in turn. Mutates the live <see cref="Plugin.Configuration"/> directly and commits through
/// <see cref="Config.ConfigCommitter"/>, the same path the settings window uses.
/// </summary>
internal sealed class GroupMembershipActions
{
    private static readonly Regex CollapseWhitespace = new(@"\s+");

    private readonly Plugin plugin;
    private readonly string name;
    private readonly string? world;

    public GroupMembershipActions(Plugin plugin, string name, string? world)
    {
        this.plugin = plugin;
        this.name = CollapseWhitespace.Replace(name.Trim(), " ");
        this.world = string.IsNullOrWhiteSpace(world) ? null : CollapseWhitespace.Replace(world.Trim(), " ");
    }

    public bool IsInGroup(PlayerGroup group) => group.Members.Any(Matches);

    /// <returns>False if the player was already in the group (no-op, not persisted).</returns>
    public bool AddToGroup(PlayerGroup group)
    {
        if (IsInGroup(group))
            return false;

        group.Members.Add(new GroupMember(name, world));
        Persist();
        return true;
    }

    /// <returns>False if the player wasn't in the group (no-op, not persisted).</returns>
    public bool RemoveFromGroup(PlayerGroup group)
    {
        if (group.Members.RemoveAll(Matches) == 0)
            return false;

        Persist();
        return true;
    }

    /// <summary>
    /// One "Add to"/"Remove from" toggle per custom group, labeled by the player's current
    /// membership — shared by the native right-click menu and Chat 2's integration menu.
    /// </summary>
    public IEnumerable<(string Label, Action Toggle)> MenuEntries()
    {
        foreach (var group in plugin.Configuration.Groups.Groups)
        {
            if (IsInGroup(group))
                yield return (string.Format(Loc.Get("Groups_ContextMenu_RemoveFrom"), group.Name), () => RemoveFromGroup(group));
            else
                yield return (string.Format(Loc.Get("Groups_ContextMenu_AddTo"), group.Name), () => AddToGroup(group));
        }
    }

    /// <summary>
    /// Delegates to <see cref="GroupMatcher.IsMember"/> — literally the same folding (NFKC +
    /// case-insensitive) and bare-world-wildcard rules chat recoloring uses, so a group's
    /// "Add"/"Remove" wording and the actual match always agree.
    /// </summary>
    private bool Matches(GroupMember member) => GroupMatcher.IsMember(member, name, world);

    /// <summary>
    /// Empties a group's member list. Static — clear targets no player, so there is no
    /// (name, world) pair to bind — but persists through the same path as add/remove.
    /// </summary>
    /// <returns>False if the group was already empty (no-op, not persisted).</returns>
    public static bool ClearGroup(Plugin plugin, PlayerGroup group)
    {
        if (group.Members.Count == 0)
            return false;

        group.Members.Clear();
        Persist(plugin);
        return true;
    }

    /// <summary>
    /// Shared "Name [World]" display form for chat-command output and the Groups tab;
    /// an absent or empty world renders the bare name.
    /// </summary>
    internal static string FormatPlayer(string name, string? world)
        => string.IsNullOrEmpty(world) ? name : $"{name} [{world}]";

    private void Persist() => Persist(plugin);

    private static void Persist(Plugin plugin) => plugin.ConfigCommitter.CommitIfChanged();
}
