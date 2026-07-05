using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace GobchatEx.Chat;

/// <summary>One nearby player's distance, captured by <see cref="SenderDistance.Snapshot"/>.</summary>
internal readonly record struct PlayerDistance(string Name, string? HomeWorld, float Distance);

/// <summary>
/// Resolves a chat sender's distance to the local player from <see cref="Plugin.ObjectTable"/>
/// positions at message time (replaces the app's memory-reader actor manager). Returns null when
/// the sender — or the local player — isn't in the object table; the range filter leaves such
/// messages fully visible, the app's deliberate "unknown sender stays visible" rule. Must run on
/// the framework thread (the object table throws otherwise); the CheckMessageHandled pass the
/// range filter runs on already is.
/// </summary>
internal static class SenderDistance
{
    public static float? Resolve(string name, string? world)
    {
        var local = Plugin.ObjectTable.LocalPlayer;
        if (local == null)
            return null;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IPlayerCharacter pc)
                continue;

            if (!pc.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            // A sender world is only present for cross-world senders; when it is, the character's
            // home world must agree — two visitors with the same name from different worlds can
            // stand in the same crowd.
            if (world != null)
            {
                var homeWorld = pc.HomeWorld.ValueNullable?.Name.ExtractText();
                if (!world.Equals(homeWorld, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            return Vector3.Distance(local.Position, pc.Position);
        }

        return null;
    }

    /// <summary>
    /// The object table's players and their distances as plain data, for lookups off the
    /// framework thread (ChatTwoStyleProvider's message-thread evaluation). Must run on the
    /// framework thread like <see cref="Resolve"/>; empty when not logged in.
    /// </summary>
    public static List<PlayerDistance> Snapshot()
    {
        var players = new List<PlayerDistance>();

        var local = Plugin.ObjectTable.LocalPlayer;
        if (local == null)
            return players;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IPlayerCharacter pc)
                continue;

            players.Add(new PlayerDistance(
                pc.Name.TextValue,
                pc.HomeWorld.ValueNullable?.Name.ExtractText(),
                Vector3.Distance(local.Position, pc.Position)));
        }

        return players;
    }

    /// <summary>
    /// <see cref="Resolve"/>'s matching rules over a captured snapshot instead of the live object
    /// table: name match required, and a known sender world must agree with the home world (two
    /// same-named visitors from different worlds can stand in the same crowd). Thread-free — safe
    /// from any thread as long as the snapshot reference was published after construction.
    /// </summary>
    public static float? ResolveFrom(List<PlayerDistance> players, string name, string? world)
    {
        foreach (var player in players)
        {
            if (!player.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (world != null && !world.Equals(player.HomeWorld, StringComparison.OrdinalIgnoreCase))
                continue;

            return player.Distance;
        }

        return null;
    }
}
