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
    public static PlayerCommandVerb Parse(string args)
    {
        var trimmed = args.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var verb = firstSpace < 0 ? trimmed : trimmed[..firstSpace];

        switch (verb.ToLowerInvariant())
        {
            case "count":
                return new PlayerCommandVerb(PlayerCommandVerbKind.Count, string.Empty);
            case "list":
                return new PlayerCommandVerb(PlayerCommandVerbKind.List, string.Empty);
            case "distance":
                var rest = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..];
                return new PlayerCommandVerb(PlayerCommandVerbKind.Distance, rest);
            default:
                return new PlayerCommandVerb(PlayerCommandVerbKind.Invalid, string.Empty);
        }
    }
}
