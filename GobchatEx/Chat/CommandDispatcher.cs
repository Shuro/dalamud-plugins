using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Routes "/gex ..." (and its "/gobchatex"/"/gobchat" aliases, plus the opt-in legacy "/e gc ..."
/// echo form via <see cref="LegacyCommandListener"/>) to the right subcommand handler. Shared between
/// <see cref="Plugin.OnCommand"/> and <see cref="LegacyCommandListener"/> so both entry points behave
/// identically. Empty args opens the settings window; any word that isn't recognized — including the
/// old app's retired "profile"/"info"/"error"/"close" commands and plain typos — reports "Unknown
/// command" rather than silently doing nothing, so retired commands are never mistaken for working
/// ones. The routing decision itself lives in <see cref="CommandRouter"/> (Dalamud-free, unit
/// tested); this class is the thin shell that resolves "&lt;t&gt;" and carries out each decision.
/// </summary>
internal static class CommandDispatcher
{
    // FFXIV's own macro-placeholder substitution (<t>, <mo>, ...) only fires for commands the game's
    // native text-command processor recognizes — a real "/echo" line gets <t> replaced before it ever
    // reaches chat, which is why LegacyCommandListener's echoed "/e gc ..." text already has it
    // resolved. Dalamud's ICommandManager hook intercepts "/gex ..." with the raw, unsubstituted text,
    // so <t> has to be resolved here ourselves — once, centrally, so every subcommand that takes a
    // player name (group add/remove, player distance, ...) benefits without each handler doing this
    // itself. A no-op on already-substituted text (the legacy path), since there's no literal "<t>"
    // left to match.
    private static readonly Regex TargetPlaceholder = new("<t>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Execute(Plugin plugin, string args)
    {
        var route = CommandRouter.Parse(ResolveTargetPlaceholder(args));

        switch (route.Kind)
        {
            case CommandRouteKind.ToggleSettings:
                plugin.ToggleSettingsUI();
                break;
            case CommandRouteKind.Group:
                GroupCommandHandler.Execute(plugin, route.Rest);
                break;
            case CommandRouteKind.Player:
                PlayerCommandHandler.Execute(plugin, route.Rest);
                break;
            case CommandRouteKind.Help:
                PrintHelp();
                break;
            case CommandRouteKind.ConfigOpen:
                plugin.OpenSettingsUI();
                break;
            case CommandRouteKind.Unknown:
                Plugin.ChatGui.PrintError(string.Format(Loc.Get("Commands_Unknown"), route.Rest));
                break;
        }
    }

    /// <summary>
    /// Replaces a literal "&lt;t&gt;" with the current target's "Name [World]" (bare name if the
    /// target isn't a player or has no resolvable world). Left untouched — not blanked — when there's
    /// no target, so a missing target still falls through to whatever handler owns the argument and
    /// produces its own "not found"/"invalid syntax" error rather than a confusing empty name.
    /// </summary>
    private static string ResolveTargetPlaceholder(string args)
    {
        if (!TargetPlaceholder.IsMatch(args))
            return args;

        var target = Plugin.TargetManager.Target;
        if (target == null)
            return args;

        var world = target is IPlayerCharacter pc ? pc.HomeWorld.ValueNullable?.Name.ExtractText() : null;
        var display = GroupMembershipActions.FormatPlayer(target.Name.TextValue, world);
        return TargetPlaceholder.Replace(args, _ => display);
    }

    /// <summary>
    /// One Print call per line: the native FFXIV chat log is a single-line-per-entry control, so a
    /// single "\n"-joined SeString would render as one garbled line rather than a readable list.
    /// </summary>
    private static void PrintHelp()
    {
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_Header"));
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_Help"));
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_ConfigOpen"));
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_Group"));
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_GroupList"));
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_PlayerCount"));
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_PlayerList"));
        Plugin.ChatGui.Print(Loc.Get("Commands_Help_PlayerDistance"));
    }
}
