using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Parses "/gobchat player ..." (stripped by <see cref="CommandDispatcher"/> before this is called):
/// "count" and "list" report nearby players — excluding the local player — via
/// <see cref="SenderDistance.Snapshot"/>; "distance &lt;name&gt; [world]" reports the distance to one
/// named player via <see cref="SenderDistance.Resolve"/>. Both are framework-thread-only, which is
/// safe here: <see cref="Execute"/> is only ever called from an ICommandManager callback or from
/// CheckMessageHandled (via CommandDispatcher/LegacyCommandListener), and both already run on the
/// framework thread. The "count"/"list"/"distance" verb parsing itself lives in
/// <see cref="PlayerCommandVerbParser"/> (Dalamud-free, unit tested).
/// </summary>
internal static class PlayerCommandHandler
{
    private static readonly Regex DistanceTarget = new(
        "^" + GroupCommandHandler.NameTailPattern + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Execute(Plugin plugin, string args)
    {
        var verb = PlayerCommandVerbParser.Parse(args);

        switch (verb.Kind)
        {
            case PlayerCommandVerbKind.Count:
                ExecuteCount();
                break;
            case PlayerCommandVerbKind.List:
                ExecuteList();
                break;
            case PlayerCommandVerbKind.Distance:
                ExecuteDistance(verb.Rest);
                break;
            case PlayerCommandVerbKind.Invalid:
                Plugin.ChatGui.PrintError(Loc.Get("Commands_Player_InvalidSyntax"));
                break;
        }
    }

    private static void ExecuteCount()
    {
        var count = NearbyPlayers().Count;
        Plugin.ChatGui.Print(string.Format(CultureInfo.InvariantCulture, Loc.Get("Commands_Player_Count"), count));
    }

    private static void ExecuteList()
    {
        var players = NearbyPlayers();
        if (players.Count == 0)
        {
            Plugin.ChatGui.Print(Loc.Get("Commands_Player_ListEmpty"));
            return;
        }

        var entries = players
            .OrderBy(p => p.Distance)
            .Select(p => string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1:0.00}y",
                GroupMembershipActions.FormatPlayer(p.Name, p.HomeWorld),
                p.Distance));
        Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Player_List"), string.Join(", ", entries)));
    }

    private static void ExecuteDistance(string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            Plugin.ChatGui.PrintError(Loc.Get("Commands_Player_InvalidSyntax"));
            return;
        }

        var match = DistanceTarget.Match(trimmed);
        var name = match.Success && match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : trimmed;
        var world = match.Success && match.Groups["server"].Success
            ? match.Groups["server"].Value.Trim(' ', '[', ']')
            : null;

        var distance = SenderDistance.Resolve(name, world);
        if (distance == null)
        {
            Plugin.ChatGui.PrintError(string.Format(Loc.Get("Commands_Player_DistanceNotFound"), name));
            return;
        }

        Plugin.ChatGui.Print(string.Format(CultureInfo.InvariantCulture, Loc.Get("Commands_Player_Distance"), name, distance.Value));
    }

    /// <summary>
    /// Nearby players from <see cref="SenderDistance.Snapshot"/>, excluding the local player and any
    /// entry with no resolved name yet. The object table's general enumerator only skips slots with no
    /// native address at all (see ObjectTable.CachedEntry.Update) — a player-kind slot that's mid
    /// spawn/despawn can still have a valid address but an unresolved name and a stale position, which
    /// otherwise surfaces as a nameless "player" at a nonsensical distance.
    /// </summary>
    private static List<PlayerDistance> NearbyPlayers()
    {
        var local = Plugin.ObjectTable.LocalPlayer;
        var snapshot = SenderDistance.Snapshot();
        var localWorld = local?.HomeWorld.ValueNullable?.Name.ExtractText();

        return snapshot
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Where(p => local == null
                || !(p.Name.Equals(local.Name.TextValue, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.HomeWorld, localWorld, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
