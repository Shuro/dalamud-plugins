using System.Text.RegularExpressions;

namespace GobchatEx.Core;

/// <summary>Which "/gex player ..." verb <see cref="PlayerCommandVerbParser.Parse"/> selected.</summary>
public enum PlayerCommandVerbKind
{
    /// <summary>"count" — how many players are nearby.</summary>
    Count,

    /// <summary>"list" — nearby players with their distance.</summary>
    List,

    /// <summary>"distance &lt;name&gt; [world]" — <see cref="PlayerCommandVerb.Rest"/> is the text
    /// after "distance".</summary>
    Distance,

    /// <summary>Anything else, including an empty verb.</summary>
    Invalid,
}

/// <summary>One parsed "/gex player ..." verb.</summary>
public readonly record struct PlayerCommandVerb(PlayerCommandVerbKind Kind, string Rest);

/// <summary>
/// Pure "/gex player ..." verb parsing, split out of the Dalamud-facing
/// GobchatEx.Chat.PlayerCommandHandler per ADR 0002 so it's unit-testable.
/// </summary>
public static class PlayerCommandVerbParser
{
    // "name [world]" distance target (character class ported from the app's
    // PlayerGroupCommandHandler; ´ is a literal since verbatim strings don't process escapes).
    private static readonly Regex DistanceTarget = new(
        @"^\b(?:(?<name>[ \w'`´-]+)(?<server>\s*\[\w+\])?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static PlayerCommandVerb Parse(string args)
    {
        var (verb, rest) = CommandText.SplitVerb(args);

        return verb.ToLowerInvariant() switch
        {
            "count" => new PlayerCommandVerb(PlayerCommandVerbKind.Count, string.Empty),
            "list" => new PlayerCommandVerb(PlayerCommandVerbKind.List, string.Empty),
            "distance" => new PlayerCommandVerb(PlayerCommandVerbKind.Distance, rest),
            _ => new PlayerCommandVerb(PlayerCommandVerbKind.Invalid, string.Empty),
        };
    }

    /// <summary>
    /// Splits a "distance" target into name and optional world ("Bob Smith [Zodiark]"); null when
    /// empty. Text the grammar can't split is taken whole as the name.
    /// </summary>
    public static (string Name, string? World)? ParseDistanceTarget(string rest)
    {
        var trimmed = rest.Trim();
        if (trimmed.Length == 0)
            return null;

        var match = DistanceTarget.Match(trimmed);
        var name = match.Success && match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : trimmed;
        var world = match.Success && match.Groups["server"].Success
            ? match.Groups["server"].Value.Trim(' ', '[', ']')
            : null;
        return (name, world);
    }
}
