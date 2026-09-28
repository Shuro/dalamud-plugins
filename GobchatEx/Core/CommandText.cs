namespace GobchatEx.Core;

/// <summary>Shared "/gex ..." argument splitting for the router and the subcommand verb parsers.</summary>
public static class CommandText
{
    /// <summary>
    /// Splits the trimmed <paramref name="args"/> at the first space into the verb and the text
    /// after it (empty when there is none). The verb keeps its original case.
    /// </summary>
    public static (string Verb, string Tail) SplitVerb(string args)
    {
        var trimmed = args.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, string.Empty) : (trimmed[..space], trimmed[(space + 1)..]);
    }
}
