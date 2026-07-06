using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Parses "/gobchat group ..." (and its "g" alias, stripped by <see cref="Plugin.OnCommand"/> before
/// this is called). Grammar: "&lt;idx&gt; task player [world]" or "task &lt;idx&gt; player [world]" for
/// a numeric locator — both orders, mirroring the old app's PlayerGroupCommandHandler exactly — plus
/// "&lt;name&gt; task player [world]" for a group referenced by name. Name-locator-first only: a group
/// name can contain spaces, so "task &lt;name&gt; player" would be ambiguous about where the name ends
/// and the player begins; the natural "MyGroup add Bob" reading order avoids that ambiguity entirely.
/// A purely numeric name is rejected at group-creation time (GroupsTab), so a numeric locator can never
/// be mistaken for a name. "list" prints the custom groups with their 1-based indices.
/// </summary>
internal static class GroupCommandHandler
{
    // Ported verbatim from PlayerGroupCommandHandler's name-tail pattern (the acute accent ´ is
    // written as a literal character here rather than a string escape, since verbatim strings don't
    // process backslash escapes).
    private const string NameTailPattern = @"\b(?<composite>(?<name>[ \w'`´-]+)(?<server>\s*\[\w+\])?)?";

    private static readonly Regex IndexFirst = new(
        @"(?<locator>\d+)\b\s+\b(?<task>add|remove|clear)\b\s*.*?" + NameTailPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TaskFirst = new(
        @"(?<task>add|remove|clear)\b\s+\b(?<locator>\d+)\b\s*.*?" + NameTailPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NameLocatorFirst = new(
        @"(?<locator>.+?)\s+\b(?<task>add|remove|clear)\b\s*.*?" + NameTailPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Execute(Plugin plugin, string args)
    {
        if (args.Trim().Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ListGroups(plugin);
            return;
        }

        var match = IndexFirst.Match(args);
        if (!match.Success)
            match = TaskFirst.Match(args);
        if (!match.Success)
            match = NameLocatorFirst.Match(args);

        if (!match.Success)
        {
            Plugin.ChatGui.PrintError(Loc.Get("Commands_Group_InvalidSyntax"));
            return;
        }

        var locatorText = match.Groups["locator"].Value.Trim();
        var task = match.Groups["task"].Value.ToLowerInvariant();
        var playerName = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : null;
        var playerWorld = match.Groups["server"].Success
            ? match.Groups["server"].Value.Trim(' ', '[', ']')
            : null;

        if (task != "clear" && string.IsNullOrEmpty(playerName))
        {
            Plugin.ChatGui.PrintError(Loc.Get("Commands_Group_InvalidSyntax"));
            return;
        }

        var group = ResolveGroup(plugin, locatorText);
        if (group == null)
        {
            Plugin.ChatGui.PrintError(string.Format(Loc.Get("Commands_Group_InvalidLocator"), locatorText));
            return;
        }

        switch (task)
        {
            case "clear":
                ExecuteClear(plugin, group);
                break;
            case "add":
                ExecuteAdd(plugin, group, playerName!, playerWorld);
                break;
            case "remove":
                ExecuteRemove(plugin, group, playerName!, playerWorld);
                break;
        }
    }

    private static PlayerGroup? ResolveGroup(Plugin plugin, string locatorText)
    {
        if (int.TryParse(locatorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
        {
            return idx >= 1 && idx <= plugin.Configuration.Groups.Count
                ? plugin.Configuration.Groups[idx - 1]
                : null;
        }

        return plugin.Configuration.Groups
            .FirstOrDefault(g => g.Name.Equals(locatorText, StringComparison.OrdinalIgnoreCase));
    }

    private static void ExecuteAdd(Plugin plugin, PlayerGroup group, string name, string? world)
    {
        var display = FormatPlayer(name, world);
        var actions = new GroupMembershipActions(plugin, name, world);
        Plugin.ChatGui.Print(actions.AddToGroup(group)
            ? string.Format(Loc.Get("Commands_Group_Added"), display, group.Name)
            : string.Format(Loc.Get("Commands_Group_AlreadyInGroup"), display, group.Name));
    }

    private static void ExecuteRemove(Plugin plugin, PlayerGroup group, string name, string? world)
    {
        var display = FormatPlayer(name, world);
        var actions = new GroupMembershipActions(plugin, name, world);
        Plugin.ChatGui.Print(actions.RemoveFromGroup(group)
            ? string.Format(Loc.Get("Commands_Group_Removed"), display, group.Name)
            : string.Format(Loc.Get("Commands_Group_NotInGroup"), display, group.Name));
    }

    private static void ExecuteClear(Plugin plugin, PlayerGroup group)
    {
        if (group.Members.Count == 0)
        {
            Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Group_AlreadyEmpty"), group.Name));
            return;
        }

        group.Members.Clear();
        plugin.Configuration.Save();
        plugin.ChatListener.SettingsChanged();
        Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Group_Cleared"), group.Name));
    }

    private static void ListGroups(Plugin plugin)
    {
        var groups = plugin.Configuration.Groups;
        if (groups.Count == 0)
        {
            Plugin.ChatGui.Print(Loc.Get("Commands_Group_ListEmpty"));
            return;
        }

        var entries = groups.Select((g, i) => $"{i + 1}. {g.Name}");
        Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Group_List"), string.Join(", ", entries)));
    }

    private static string FormatPlayer(string name, string? world) => world == null ? name : $"{name} [{world}]";
}
