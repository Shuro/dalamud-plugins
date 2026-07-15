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
        var trimmed = args.TrimStart();
        if (trimmed.Length == 0)
            return new CommandRoute(CommandRouteKind.ToggleSettings, string.Empty);

        var firstSpace = trimmed.IndexOf(' ');
        var firstWord = firstSpace < 0 ? trimmed : trimmed[..firstSpace];
        var rest = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..];

        if (firstWord.Equals("group", StringComparison.OrdinalIgnoreCase)
            || firstWord.Equals("g", StringComparison.OrdinalIgnoreCase))
            return new CommandRoute(CommandRouteKind.Group, rest);

        if (firstWord.Equals("player", StringComparison.OrdinalIgnoreCase)
            || firstWord.Equals("p", StringComparison.OrdinalIgnoreCase))
            return new CommandRoute(CommandRouteKind.Player, rest);

        if (firstWord.Equals("mention", StringComparison.OrdinalIgnoreCase))
            return new CommandRoute(CommandRouteKind.Mention, rest);

        if (firstWord.Equals("log", StringComparison.OrdinalIgnoreCase))
            return new CommandRoute(CommandRouteKind.Log, rest);

        if (firstWord.Equals("help", StringComparison.OrdinalIgnoreCase))
            return new CommandRoute(CommandRouteKind.Help, string.Empty);

        if (firstWord.Equals("config", StringComparison.OrdinalIgnoreCase)
            && rest.Trim().Equals("open", StringComparison.OrdinalIgnoreCase))
            return new CommandRoute(CommandRouteKind.ConfigOpen, string.Empty);

        return new CommandRoute(CommandRouteKind.Unknown, trimmed);
    }
}
