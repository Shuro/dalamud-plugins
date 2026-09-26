#if DEBUG
using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.ContextMenu;

namespace GobchatEx;

/// <summary>
/// In-memory capture of native context-menu opens for the "Groups submenu shows up in
/// Glamourlog" investigation, shown on the Debug tab's "Context menus" pane. Records before
/// <see cref="Plugin.OnMenuOpened"/>'s target filter so plugin-opened native menus
/// (KamiToolKit/AgentContext, which carry a stale target) are captured too. Deliberately not
/// persisted anywhere — dev-only diagnostics, entries vanish on plugin reload. Temporary;
/// remove together with the menu-origin gate it informs.
/// </summary>
internal static class ContextMenuProbe
{
    internal sealed record Entry(
        DateTime Time,
        ContextMenuType MenuType,
        string? AddonName,
        string TargetName,
        ulong TargetObjectId,
        ulong TargetContentId,
        string HomeWorld);

    // Bounded so a long session can't grow it without end; oldest dropped first.
    private const int MaxEntries = 200;

    private static readonly List<Entry> Entries = [];

    /// <summary>Gets the total recorded count, including entries dropped by the cap.</summary>
    internal static int Count { get; private set; }

    /// <summary>Records one menu-open event. Framework thread only (menu-open hook).</summary>
    internal static void Record(IMenuOpenedArgs args)
    {
        // Inventory menus carry a MenuTargetInventory instead; only MenuType/AddonName matter
        // for those, so the player-target columns stay empty.
        var entry = args.Target is MenuTargetDefault target
            ? new Entry(DateTime.Now, args.MenuType, args.AddonName, target.TargetName,
                target.TargetObjectId, target.TargetContentId,
                target.TargetHomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty)
            : new Entry(DateTime.Now, args.MenuType, args.AddonName,
                string.Empty, 0, 0, string.Empty);

        Entries.Add(entry);
        Count++;
        if (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
    }

    internal static Entry[] Snapshot() => [.. Entries];

    internal static void Clear()
    {
        Entries.Clear();
        Count = 0;
    }
}
#endif
