using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GobchatEx.Localization;

namespace GobchatEx.Windows;

/// <summary>
/// Compact movable overlay bar (like a game hotbar) with a start/stop button
/// for the session chat log, one-click on/off buttons for the four main
/// features, plus quick access to the settings window. Icon-only to stay
/// small; tooltips carry the names. Visibility is
/// driven entirely by <see cref="Config.GeneralConfig.ShowQuickbar"/> — see
/// <see cref="PreOpenCheck"/> — and every click persists and applies itself
/// immediately (the settings window's debounced commit is not involved).
/// </summary>
public class QuickbarWindow : Window
{
    private readonly Plugin plugin;

    // Bar size from the previous frame's layout. AlwaysAutoResize means ImGui
    // sizes the window from last frame's content anyway, so a last-frame
    // capture is exactly as fresh as the size ImGui uses this frame.
    private Vector2 lastSize;

    // Set by PreDraw, read by Draw: the grip is hidden only while actually
    // glued to a chat window this frame — not in attached-but-no-target
    // fallback mode, where the bar floats free and stays draggable.
    private bool anchoredThisFrame;

    public QuickbarWindow(Plugin plugin)
        : base("GobchatEx Quickbar###GobchatExQuickbar",
               ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
               | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
               | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDocking
               | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;   // overlay: Escape must not close it
        DisableWindowSounds = true;   // appears on login; no open/close sfx
    }

    // Config is the single source of truth for visibility. Runs every frame
    // even while closed, so it covers initial load, the General-tab toggle,
    // and the bar's own X button. Nothing else writes IsOpen.
    public override void PreOpenCheck()
    {
        var general = plugin.Configuration.General;
        IsOpen = general.ShowQuickbar;

        // Dalamud auto-hides plugin UI on user UI-hide / cutscene / gpose
        // (gated by the user's Dalamud settings), so showing the bar in those
        // states needs the matching exemption flag. Plugin-wide by necessity —
        // Dalamud has no per-window control — so the settings window inherits
        // the exemption too (same tradeoff Chat 2 makes). Everything falls
        // back to Dalamud defaults while the Quickbar is off.
        var ui = Plugin.PluginInterface.UiBuilder;
        ui.DisableUserUiHide = general.ShowQuickbar && !general.QuickbarHideWhenUiHidden;
        ui.DisableCutsceneUiHide = general.ShowQuickbar && !general.QuickbarHideDuringCutscenes;
        // Gpose is grouped with the cutscene option, like Chat 2.
        ui.DisableGposeUiHide = general.ShowQuickbar && !general.QuickbarHideDuringCutscenes;
    }

    // Hide conditions, checked without touching config or firing OnClose.
    // Manual checks even where Dalamud auto-hides (cutscene/gpose), because
    // that auto-hide can be turned off in Dalamud's own settings. UI-hidden
    // needs no check here: Dalamud stops drawing unconditionally unless the
    // DisableUserUiHide exemption above is set. Flag semantics mirror Chat 2.
    public override bool DrawConditions()
    {
        var general = plugin.Configuration.General;
        if (general.QuickbarHideWhenNotLoggedIn && !Plugin.ClientState.IsLoggedIn)
            return false;
        if (general.QuickbarHideDuringCutscenes
            && (Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
                || Plugin.Condition[ConditionFlag.WatchingCutscene]
                || Plugin.Condition[ConditionFlag.WatchingCutscene78]))
            return false;
        if (general.QuickbarHideInLoadingScreens && Plugin.Condition[ConditionFlag.BetweenAreas])
            return false;
        if (general.QuickbarHideInBattle && Plugin.Condition[ConditionFlag.InCombat])
            return false;
        // "Chat" is whichever window the bar would anchor to (Chat 2 if
        // present, else the game's chat log) — reuses the attach-target
        // resolution and applies whether or not attach mode is on.
        if (general.QuickbarHideWhenChatHidden && !TryGetChatTopLeft(out _, out _))
            return false;
        return true;
    }

    // Anchors the bar to the top edge of the chat window. Runs right before
    // Dalamud applies Position via SetNextWindowPos, and only when the window
    // will actually draw. While anchored, SetNextWindowPos(Always) also
    // refreshes ImGui's remembered position each frame, so losing the target
    // (or toggling attach off) leaves the bar exactly where it last sat.
    public override void PreDraw()
    {
        anchoredThisFrame = false;
        Position = null; // free-float: ImGui keeps its remembered position

        if (!plugin.Configuration.General.QuickbarAttachToChat
            // Fresh load with attach already on: height unknown until Draw has
            // run once (AlwaysAutoResize) — float one frame, snap from the second.
            || lastSize == default
            || !TryGetChatTopLeft(out var chatTopLeft, out var viewportTop))
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
            return;
        }

        var gap = 4f * ImGuiHelpers.GlobalScale;
        Position = new Vector2(
            chatTopLeft.X,
            // Clamp to the viewport top: with the chat flush against the
            // screen edge the bar overlaps the chat's top rows rather than
            // going off-screen.
            MathF.Max(chatTopLeft.Y - lastSize.Y - gap, viewportTop));
        PositionCondition = ImGuiCond.Always;
        // Body-dragging would fight the per-frame repositioning; NoMove is
        // lifted again on every non-anchored path above.
        Flags |= ImGuiWindowFlags.NoMove;
        anchoredThisFrame = true;
    }

    // Chat 2's ImGui window replaces the vanilla log when present, so it wins;
    // otherwise the game's own ChatLog addon; otherwise no anchor. Both reads
    // are per-frame polls — Dalamud has no move/resize events for either.
    private static bool TryGetChatTopLeft(out Vector2 topLeft, out float viewportTop)
    {
        // All Dalamud plugins share one ImGui context, so Chat 2's main window
        // is directly visible to us. ImGui hashes only the part after "###",
        // making this match "Chat 2###chat2" regardless of the visible title;
        // popout tabs have different IDs and are deliberately ignored. No
        // is-Chat-2-loaded pre-check: the lookup is a hash-table miss when the
        // plugin is absent. WasActive (submitted last frame) instead of Active
        // because plugin draw order within a frame is undefined.
        var chat2 = ImGuiP.FindWindowByName("###chat2");
        if (!chat2.IsNull && chat2.WasActive && !chat2.Hidden && !chat2.Collapsed)
        {
            topLeft = chat2.Pos;
            // Chat 2 can sit on a secondary-monitor viewport; clamp against
            // the viewport it is actually on.
            viewportTop = chat2.Viewport.IsNull
                ? ImGuiHelpers.MainViewport.Pos.Y
                : chat2.Viewport.ImGuiViewport.Pos.Y;
            return true;
        }

        // Main ChatLog addon only; the extra tab panels (ChatLogPanel_0..3)
        // are separate addons and deliberately ignored. Addon coordinates are
        // game-window-relative — offset by the main viewport to get ImGui
        // global coordinates (zero unless multi-viewports are enabled).
        var addon = Plugin.GameGui.GetAddonByName("ChatLog");
        if (addon.IsReady && addon.IsVisible)
        {
            topLeft = ImGuiHelpers.MainViewport.Pos + addon.Position;
            viewportTop = ImGuiHelpers.MainViewport.Pos.Y;
            return true;
        }

        topLeft = default;
        viewportTop = 0f;
        return false;
    }

    public override void Draw()
    {
        // AlwaysAutoResize: the current size only exists after layout; capture
        // it for next frame's anchor math in PreDraw.
        lastSize = ImGui.GetWindowSize();

        // Nothing to drag while glued — the anchor overrides the position
        // every frame. The grip returns in attached-but-no-target fallback mode.
        if (!anchoredThisFrame)
        {
            DrawGrip();
            ImGui.SameLine();
        }

        // Session-scoped runtime state on the ChatLogger, not config — same start/stop logic as
        // the Logs tab's button, and nothing to persist or apply.
        var chatLogger = plugin.ChatLogger;
        if (FeatureButton(FontAwesomeIcon.FileSignature, chatLogger.IsLogging))
        {
            if (chatLogger.IsLogging)
                chatLogger.StopLogging();
            else
                chatLogger.StartLogging();
        }
        SettingsUi.Tooltip(Loc.Get(chatLogger.IsLogging
            ? "Quickbar_StopLog_Tooltip"
            : "Quickbar_StartLog_Tooltip"));

        VerticalDivider();

        var formatting = plugin.Configuration.Formatting;
        if (FeatureButton(FontAwesomeIcon.Font, formatting.RpHighlightEnabled))
        {
            formatting.RpHighlightEnabled = !formatting.RpHighlightEnabled;
            PersistAndApply();
        }
        SettingsUi.Tooltip(Loc.Get("Formatting_TabName"));
        ImGui.SameLine();

        var mentions = plugin.Configuration.Mentions;
        if (FeatureButton(FontAwesomeIcon.At, mentions.MentionsEnabled))
        {
            mentions.MentionsEnabled = !mentions.MentionsEnabled;
            PersistAndApply();
        }
        SettingsUi.Tooltip(Loc.Get("Mentions_TabName"));
        ImGui.SameLine();

        var groups = plugin.Configuration.Groups;
        if (FeatureButton(FontAwesomeIcon.Users, groups.GroupsEnabled))
        {
            groups.GroupsEnabled = !groups.GroupsEnabled;
            PersistAndApply();
        }
        SettingsUi.Tooltip(Loc.Get("Groups_TabName"));
        ImGui.SameLine();

        var range = plugin.Configuration.RangeFilter;
        if (FeatureButton(FontAwesomeIcon.Ruler, range.RangeFilterEnabled))
        {
            range.RangeFilterEnabled = !range.RangeFilterEnabled;
            PersistAndApply();
        }
        SettingsUi.Tooltip(Loc.Get("Range_TabName"));

        VerticalDivider();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
            plugin.OpenSettingsUI();
        SettingsUi.Tooltip(Loc.Get("Quickbar_OpenSettings_Tooltip"));
        ImGui.SameLine();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
        {
            plugin.Configuration.General.ShowQuickbar = false; // PreOpenCheck closes next frame
            PersistAndApply();
        }
        SettingsUi.Tooltip(Loc.Get("Quickbar_Hide_Tooltip"));
    }

    private void DrawGrip()
    {
        // Glyph in the theme's disabled-text gray so the grip reads as a
        // handle, not another (clickable-looking) button.
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Button, Vector4.Zero)
                   .Push(ImGuiCol.ButtonHovered, Vector4.Zero)
                   .Push(ImGuiCol.ButtonActive, Vector4.Zero)
                   .Push(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            ImGui.Button(FontAwesomeIcon.GripVertical.ToIconString() + "##grip");

        // Manual drag — immune to Dalamud's "move windows from title bar only"
        // setting, which would otherwise leave a titlebar-less bar unmovable.
        if (ImGui.IsItemActive())
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
    }

    /// <summary>
    /// Icon button whose background shows the feature's on/off state using the
    /// same green/red palette as the settings window's toggle switches.
    /// </summary>
    private static bool FeatureButton(FontAwesomeIcon icon, bool enabled)
    {
        using var colors = ImRaii
            .PushColor(ImGuiCol.Button, enabled ? SettingsUi.ToggleOnTrack : SettingsUi.ToggleOffTrack)
            .Push(ImGuiCol.ButtonHovered, enabled ? SettingsUi.ToggleOnTrackHover : SettingsUi.ToggleOffTrackHover)
            .Push(ImGuiCol.ButtonActive, enabled ? SettingsUi.ToggleOnTrack : SettingsUi.ToggleOffTrack);
        return ImGuiComponents.IconButton(icon);
    }

    // The bindings expose no vertical separator, so draw one ourselves.
    private static void VerticalDivider()
    {
        ImGui.SameLine();
        var p = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        var inset = 2f * ImGuiHelpers.GlobalScale;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(p.X, p.Y + inset), new Vector2(p.X, p.Y + height - inset),
            ImGui.GetColorU32(ImGuiCol.Separator));
        ImGui.Dummy(new Vector2(1f * ImGuiHelpers.GlobalScale, height));
        ImGui.SameLine();
    }

    // Mirrors GroupMembershipActions.Persist: writers outside the settings
    // window persist and apply on their own. An open SettingsWindow's debounced
    // commit just redundantly re-commits identical JSON — harmless.
    private void PersistAndApply()
    {
        plugin.Configuration.Save();
        plugin.ChatListener.SettingsChanged();
        plugin.ChatTwoStyles.SettingsChanged();
    }
}
