namespace GobchatEx.Core;

/// <summary>Which "/gex mention ..." verb <see cref="MentionCommandVerbParser.Parse"/> selected.</summary>
public enum MentionCommandVerbKind
{
    /// <summary>"add &lt;word&gt;" — <see cref="MentionCommandVerb.Rest"/> is the text after "add".</summary>
    Add,

    /// <summary>"remove &lt;word&gt;" — <see cref="MentionCommandVerb.Rest"/> is the text after
    /// "remove".</summary>
    Remove,

    /// <summary>"list" — print the configured trigger words.</summary>
    List,

    /// <summary>Anything else, including an empty verb.</summary>
    Invalid,
}

/// <summary>One parsed "/gex mention ..." verb.</summary>
public readonly record struct MentionCommandVerb(MentionCommandVerbKind Kind, string Rest);

/// <summary>
/// Pure "/gex mention ..." verb parsing, split out of the Dalamud-facing
/// GobchatEx.Chat.MentionCommandHandler per ADR 0002 so it's unit-testable.
/// </summary>
public static class MentionCommandVerbParser
{
    public static MentionCommandVerb Parse(string args)
    {
        var trimmed = args.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var verb = firstSpace < 0 ? trimmed : trimmed[..firstSpace];
        var rest = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..];

        return verb.ToLowerInvariant() switch
        {
            "add" => new MentionCommandVerb(MentionCommandVerbKind.Add, rest),
            "remove" => new MentionCommandVerb(MentionCommandVerbKind.Remove, rest),
            "list" => new MentionCommandVerb(MentionCommandVerbKind.List, string.Empty),
            _ => new MentionCommandVerb(MentionCommandVerbKind.Invalid, string.Empty),
        };
    }
}
