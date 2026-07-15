namespace GobchatEx.Core;

/// <summary>Which "/gex log ..." verb <see cref="LogCommandVerbParser.Parse"/> selected.</summary>
public enum LogCommandVerbKind
{
    /// <summary>"start" — begin logging this session.</summary>
    Start,

    /// <summary>"stop" — stop logging.</summary>
    Stop,

    /// <summary>"status" — report whether logging is running and where.</summary>
    Status,

    /// <summary>Anything else, including an empty verb.</summary>
    Invalid,
}

/// <summary>
/// Pure "/gex log ..." verb parsing, split out of the Dalamud-facing
/// GobchatEx.Chat.LogCommandHandler per ADR 0002 so it's unit-testable. No log verb takes
/// arguments, so unlike <see cref="PlayerCommandVerbParser"/> there is no Rest to carry —
/// text after the verb is ignored.
/// </summary>
public static class LogCommandVerbParser
{
    public static LogCommandVerbKind Parse(string args)
    {
        var trimmed = args.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var verb = firstSpace < 0 ? trimmed : trimmed[..firstSpace];

        return verb.ToLowerInvariant() switch
        {
            "start" => LogCommandVerbKind.Start,
            "stop" => LogCommandVerbKind.Stop,
            "status" => LogCommandVerbKind.Status,
            _ => LogCommandVerbKind.Invalid,
        };
    }
}
