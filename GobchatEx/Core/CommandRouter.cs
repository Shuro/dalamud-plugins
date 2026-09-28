using System;

namespace GobchatEx.Core;

/// <summary>Which "/gex ..." branch <see cref="CommandRouter.Parse"/> selected.</summary>
public enum CommandRouteKind
{
    /// <summary>Empty args: open (toggle) the settings window.</summary>
    ToggleSettings,

    /// <summary>"group"/"g" — <see cref="CommandRoute.Rest"/> is the text after that word.</summary>
    Group,

    /// <summary>"player"/"p" — <see cref="CommandRoute.Rest"/> is the text after that word.</summary>
    Player,

    /// <summary>"mention" — <see cref="CommandRoute.Rest"/> is the text after that word.</summary>
    Mention,

    /// <summary>"log" — <see cref="CommandRoute.Rest"/> is the text after that word.</summary>
    Log,

    /// <summary>"help" — print the command list.</summary>
    Help,

    /// <summary>"config open" — open (and focus) the settings window.</summary>
    ConfigOpen,

    /// <summary>Anything else — <see cref="CommandRoute.Rest"/> is the full trimmed input, for the
    /// "Unknown command "{0}"" message.</summary>
    Unknown,
}

/// <summary>One parsed "/gex ..." routing decision.</summary>
public readonly record struct CommandRoute(CommandRouteKind Kind, string Rest);

/// <summary>
/// Pure "/gex ..." routing: given the already-&lt;t&gt;-resolved argument text, decides which
/// subcommand handler should run and what's left of the args for it. Kept Dalamud-free per ADR
/// 0002 so this decision is unit-testable; GobchatEx.Chat.CommandDispatcher is the thin
/// Dalamud-calling shell around it.
/// </summary>
public static class CommandRouter
{
    public static CommandRoute Parse(string args)
    {
        var (verb, rest) = CommandText.SplitVerb(args);

        return verb.ToLowerInvariant() switch
        {
            "" => new CommandRoute(CommandRouteKind.ToggleSettings, string.Empty),
            "group" or "g" => new CommandRoute(CommandRouteKind.Group, rest),
            "player" or "p" => new CommandRoute(CommandRouteKind.Player, rest),
            "mention" => new CommandRoute(CommandRouteKind.Mention, rest),
            "log" => new CommandRoute(CommandRouteKind.Log, rest),
            "help" => new CommandRoute(CommandRouteKind.Help, string.Empty),
            "config" when rest.Trim().Equals("open", StringComparison.OrdinalIgnoreCase)
                => new CommandRoute(CommandRouteKind.ConfigOpen, string.Empty),
            _ => new CommandRoute(CommandRouteKind.Unknown, args.Trim()),
        };
    }
}
