using System;
using System.Linq;
using GobchatEx.Config;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Executes "/gobchat group ..." (and its "g" alias, stripped by <see cref="CommandDispatcher"/> before
/// this is called). Parsing lives in <see cref="GroupCommandParser"/> (Dalamud-free, unit tested):
/// "&lt;idx&gt; task player [world]", "task &lt;idx&gt; player [world]", or "&lt;name&gt; task player
/// [world]". A purely numeric name is rejected at group-creation time (GroupsTab), so a numeric
/// locator can never be mistaken for a name. "list" prints the custom groups with their 1-based indices.
/// </summary>
internal static class GroupCommandHandler
{
    public static void Execute(Plugin plugin, string args)
    {
        if (GroupCommandParser.IsList(args))
        {
            ListGroups(plugin);
            return;
        }

        var command = GroupCommandParser.Parse(args, name => FindByName(plugin, name) != null);
        if (command == null || (command.Task != GroupCommandTask.Clear && command.PlayerName == null))
        {
            Plugin.ChatGui.PrintError(Loc.Get("Commands_Group_InvalidSyntax"));
            return;
        }

        var group = ResolveGroup(plugin, command);
        if (group == null)
        {
            Plugin.ChatGui.PrintError(string.Format(Loc.Get("Commands_Group_InvalidLocator"), command.Locator));
            return;
        }

        switch (command.Task)
        {
            case GroupCommandTask.Clear:
                ExecuteClear(plugin, group);
                break;
            case GroupCommandTask.Add:
                ExecuteAdd(plugin, group, command.PlayerName!, command.PlayerWorld);
                break;
            case GroupCommandTask.Remove:
                ExecuteRemove(plugin, group, command.PlayerName!, command.PlayerWorld);
                break;
        }
    }

    private static PlayerGroup? ResolveGroup(Plugin plugin, GroupCommand command)
    {
        var groups = plugin.Configuration.Groups.Groups;
        if (command.Index is { } idx)
            return idx >= 1 && idx <= groups.Count ? groups[idx - 1] : null;

        return command.GroupName is { } name ? FindByName(plugin, name) : null;
    }

    private static PlayerGroup? FindByName(Plugin plugin, string name)
        => plugin.Configuration.Groups.Groups
            .FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void ExecuteAdd(Plugin plugin, PlayerGroup group, string name, string? world)
    {
        var display = GroupMembershipActions.FormatPlayer(name, world);
        var actions = new GroupMembershipActions(plugin, name, world);
        Plugin.ChatGui.Print(actions.AddToGroup(group)
            ? string.Format(Loc.Get("Commands_Group_Added"), display, group.Name)
            : string.Format(Loc.Get("Commands_Group_AlreadyInGroup"), display, group.Name));
    }

    private static void ExecuteRemove(Plugin plugin, PlayerGroup group, string name, string? world)
    {
        var display = GroupMembershipActions.FormatPlayer(name, world);
        var actions = new GroupMembershipActions(plugin, name, world);
        Plugin.ChatGui.Print(actions.RemoveFromGroup(group)
            ? string.Format(Loc.Get("Commands_Group_Removed"), display, group.Name)
            : string.Format(Loc.Get("Commands_Group_NotInGroup"), display, group.Name));
    }

    private static void ExecuteClear(Plugin plugin, PlayerGroup group)
    {
        Plugin.ChatGui.Print(GroupMembershipActions.ClearGroup(plugin, group)
            ? string.Format(Loc.Get("Commands_Group_Cleared"), group.Name)
            : string.Format(Loc.Get("Commands_Group_AlreadyEmpty"), group.Name));
    }

    private static void ListGroups(Plugin plugin)
    {
        var groups = plugin.Configuration.Groups.Groups;
        if (groups.Count == 0)
        {
            Plugin.ChatGui.Print(Loc.Get("Commands_Group_ListEmpty"));
            return;
        }

        var entries = groups.Select((g, i) => $"{i + 1}. {g.Name}");
        Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Group_List"), string.Join(", ", entries)));
    }

}
