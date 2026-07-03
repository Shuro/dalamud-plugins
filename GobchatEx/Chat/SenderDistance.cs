using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace GobchatEx.Chat;

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
}
