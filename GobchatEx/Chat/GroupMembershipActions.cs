using System.Linq;
using System.Text.RegularExpressions;
using GobchatEx.Config;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Shared add/remove/membership-check logic for one player against a custom group's member list, used
/// by the slash command, the native right-click context menu, and the Chat 2 IPC integration alike.
/// Bound to one (name, world) pair at construction (the right-clicked or /gobchat-group-targeted
/// player); callers iterate <see cref="GroupsConfig.Groups"/> and pass each <see cref="PlayerGroup"/>
/// in turn. Mutates the live <see cref="Plugin.Configuration"/> directly and persists + applies via
/// <see cref="Persist"/> — the same instance the settings window edits, so there is no conflict if
/// the window happens to be open (its instant-apply commit would just re-save the same state).
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
    /// Delegates to <see cref="GroupMatcher.IsMember"/> — literally the same folding (NFKC +
    /// case-insensitive) and bare-world-wildcard rules chat recoloring uses, so a group's
    /// "Add"/"Remove" wording and the actual match always agree.
    /// </summary>
    private bool Matches(GroupMember member) => GroupMatcher.IsMember(member, name, world);

    private void Persist()
    {
        plugin.Configuration.Save();
        plugin.ChatListener.SettingsChanged();
        plugin.ChatTwoStyles.SettingsChanged();
    }
}
