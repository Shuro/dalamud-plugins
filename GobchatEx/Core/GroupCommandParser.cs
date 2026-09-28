using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GobchatEx.Core;

/// <summary>Which membership change a "/gex group ..." command asks for.</summary>
public enum GroupCommandTask
{
    Add,
    Remove,
    Clear,
}

/// <summary>
/// One parsed "/gex group ..." membership command. Exactly one of <see cref="Index"/> (1-based,
/// numeric locator) and <see cref="GroupName"/> (name locator) is set; <see cref="Locator"/> is the
/// raw locator text for error messages. <see cref="PlayerName"/> is null when no player was given
/// (valid only for <see cref="GroupCommandTask.Clear"/> — the handler owns that check).
/// </summary>
public sealed record GroupCommand(
    GroupCommandTask Task,
    string Locator,
    int? Index,
    string? GroupName,
    string? PlayerName,
    string? PlayerWorld);

/// <summary>
/// Pure "/gex group ..." parsing, split out of the Dalamud-facing GobchatEx.Chat.GroupCommandHandler
/// per ADR 0002 so it's unit-testable. Grammar (the old app's PlayerGroupCommandHandler forms):
/// "&lt;idx&gt; task player [world]", "task &lt;idx&gt; player [world]", and
/// "&lt;name&gt; task player [world]". Every form is anchored to the whole input: an unanchored
/// numeric form used to match inside a name locator ("Team 2 add Bob" → group #2).
/// </summary>
public static class GroupCommandParser
{
    /// <summary>
    /// "name [world]" player tail (character class ported from PlayerGroupCommandHandler; the acute
    /// accent ´ is a literal character since verbatim strings don't process escapes). Shared with
    /// PlayerCommandHandler's "distance" target, which anchors it the same way.
    /// </summary>
    public const string NameTailPattern = @"\b(?<composite>(?<name>[ \w'`´-]+)(?<server>\s*\[\w+\])?)?";

    // Optional player after the task: a name (lazy, so an optional "[World]" can follow), then
    // end of input. Stricter than the old ".*?" junk-skipping tail on purpose — an anchored
    // grammar must not silently drop unparseable text.
    private const string PlayerTail = @"(?:\s+(?<name>[\w'`´-][ \w'`´-]*?))?(?:\s*\[(?<world>\w+)\])?\s*$";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex IndexFirst = new(
        @"^\s*(?<locator>\d+)\s+(?<task>add|remove|clear)\b" + PlayerTail, Options);

    private static readonly Regex TaskFirst = new(
        @"^\s*(?<task>add|remove|clear)\s+(?<locator>\d+)\b" + PlayerTail, Options);

    // Candidate split points for the name-locator form; a group name may itself contain spaces
    // and even a task word, so every occurrence is tried (see Parse).
    private static readonly Regex TaskWord = new(@"\s+(?<task>add|remove|clear)\b", Options);

    private static readonly Regex Tail = new("^" + PlayerTail, Options);

    public static bool IsList(string args) => args.Trim().Equals("list", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a membership command; null when no form matches (invalid syntax).
    /// <paramref name="groupNameExists"/> disambiguates the name-locator form: the first split
    /// whose locator names an existing group wins ("Add Squad add Bob" → group "Add Squad"); with
    /// none, the leftmost split is returned so the caller can report its locator as unknown.
    /// </summary>
    public static GroupCommand? Parse(string args, Func<string, bool> groupNameExists)
    {
        if (IndexFirst.Match(args) is { Success: true } indexFirst)
            return FromNumeric(indexFirst);
        if (TaskFirst.Match(args) is { Success: true } taskFirst)
            return FromNumeric(taskFirst);

        GroupCommand? fallback = null;
        foreach (Match taskWord in TaskWord.Matches(args))
        {
            var locator = args[..taskWord.Index].Trim();
            if (locator.Length == 0)
                continue;

            var tail = Tail.Match(args[(taskWord.Index + taskWord.Length)..]);
            if (!tail.Success)
                continue;

            var command = Build(taskWord.Groups["task"].Value, locator, index: null, groupName: locator, tail);
            if (groupNameExists(locator))
                return command;
            fallback ??= command;
        }

        return fallback;
    }

    private static GroupCommand FromNumeric(Match match)
    {
        var locator = match.Groups["locator"].Value;
        // \d+ can overflow int; an unparseable index is simply out of range (never a valid group).
        int? index = int.TryParse(locator, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        return Build(match.Groups["task"].Value, locator, index, groupName: null, match);
    }

    private static GroupCommand Build(string task, string locator, int? index, string? groupName, Match tail)
    {
        var name = tail.Groups["name"].Success ? tail.Groups["name"].Value.Trim() : null;
        var world = tail.Groups["world"].Success ? tail.Groups["world"].Value : null;
        return new GroupCommand(ParseTask(task), locator, index, groupName,
            string.IsNullOrEmpty(name) ? null : name, world);
    }

    private static GroupCommandTask ParseTask(string task) => task.ToLowerInvariant() switch
    {
        "add" => GroupCommandTask.Add,
        "remove" => GroupCommandTask.Remove,
        _ => GroupCommandTask.Clear,
    };
}
