using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GobchatEx.Localization;
using GobchatEx.Windows.SettingsTabs;

namespace GobchatEx.Windows;

/// <summary>
/// Settings window with a sectioned nav rail mirroring the standalone
/// GobchatEx app (General / Appearance / Chat, divider, About); pages
/// without plugin functionality yet are placeholders. Right side is the
/// page content, footer has Save (persist + close), Apply (persist) and
/// Cancel (discard + close); a Ko-fi heart sits in the title bar. Edits
/// are staged into a mutable Configuration copy and only persisted +
/// applied when the user saves or applies.
/// </summary>
public class SettingsWindow : Window
{
    private const string KofiUrl = "https://ko-fi.com/shuro2005";

    // Ko-fi brand red (#FF5E5B) for the title bar heart.
    private static readonly Vector4 KofiIconColor = new(1f, 94f / 255f, 91f / 255f, 1f);

    /// <summary>
    /// Keeps Ko-fi left of Dalamud's own pin/clickthrough/blur "options" button (fixed at
    /// Priority 0, shown only if the player enabled it in Dalamud's settings) instead of an
    /// unstable tie — per <see cref="TitleBarButton.Priority"/>, lower draws closer to the native
    /// collapse/close buttons on the right, so Ko-fi needs the higher value to land further left.
    /// Native collapse (no custom button needed — see the ctor's Flags) sits between the options
    /// button and Close, giving: Ko-fi, [Dalamud options], collapse, Close.
    /// </summary>
    private const int KofiPriority = 10;

    /// <summary>
    /// One nav-rail section: a header above its pages, or a divider when
    /// <paramref name="HeaderKey"/> is null (the app does this before About).
    /// </summary>
    private sealed record NavSection(string? HeaderKey, ISettingsTab[] Tabs);

    private readonly Plugin plugin;
    private readonly Configuration mutable;
    private readonly List<NavSection> sections;
    private ISettingsTab currentTab;

    public SettingsWindow(Plugin plugin)
        : base("GobchatEx Roleplay Suite###GobchatExSettings",
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(550, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(600, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        mutable = new Configuration();
        mutable.UpdateFrom(plugin.Configuration);

        sections =
        [
            new NavSection("Settings_Nav_General",
            [
                new GeneralTab(mutable, plugin.ChatTwoStyles),
                new PlaceholderTab("Placeholder_Profiles_Name", FontAwesomeIcon.Users,
                    "Placeholder_Profiles_Description"),
                new PlaceholderTab("Placeholder_Logs_Name", FontAwesomeIcon.FileAlt,
                    "Placeholder_Logs_Description"),
            ]),
            new NavSection("Settings_Nav_Appearance",
            [
                new FormattingTab(mutable),
            ]),
            new NavSection("Settings_Nav_Chat",
            [
                new MentionsTab(mutable),
                new GroupsTab(mutable, plugin.ChatTwoStyles),
                new RangeTab(mutable, plugin.ChatTwoStyles),
                new ChatTwoTab(mutable, plugin.ChatTwoStyles),
            ]),
            new NavSection(null,
            [
#if DEBUG
                new DebugTab(plugin),
#endif
                new AboutTab(),
            ]),
        ];
        currentTab = sections[0].Tabs[0];

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Heart,
            IconColor = KofiIconColor,
            IconOffset = new Vector2(1.5f, 1f),
            Priority = KofiPriority,
            Click = _ => Dalamud.Utility.Util.OpenLink(KofiUrl),
            ShowTooltip = () =>
            {
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted(Loc.Get("Settings_KofiTooltip"));
            },
        });
    }

    public override void PreDraw()
    {
        WindowName = $"{Loc.Get("Settings_WindowTitle")}###GobchatExSettings";

        // Reads the saved value, not the staged copy: toggling "movable"
        // should only take effect once the user hits Save.
        if (plugin.Configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        // Re-stage from the saved config whenever the window (re)opens, so
        // Cancel or closing via X implicitly reverts unsaved edits.
        if (ImGui.IsWindowAppearing())
            mutable.UpdateFrom(plugin.Configuration);

        using (var table = ImRaii.Table("##gobchatex-settings-table", 2, ImGuiTableFlags.BordersInnerV))
        {
            if (table)
            {
                ComputeNavWidths(out var iconColumnWidth, out var railWidth);
                ImGui.TableSetupColumn("tab", ImGuiTableColumnFlags.WidthFixed, railWidth);
                ImGui.TableSetupColumn("settings", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextColumn();
                DrawNavRail(iconColumnWidth);

                ImGui.TableNextColumn();

                // Reserve one line below the tab content for the footer row
                // (height formula from ChatTwo's settings window).
                var style = ImGui.GetStyle();
                var height = ImGui.GetContentRegionAvail().Y - style.FramePadding.Y * 2
                    - style.ItemSpacing.Y - style.ItemInnerSpacing.Y * 2 - ImGui.CalcTextSize("A").Y;

                using var child = ImRaii.Child("##gobchatex-settings-tab", new Vector2(-1, height));
                if (child)
                    currentTab.Draw();
            }
        }

        ImGui.Separator();
        DrawFooter();
    }

    /// <summary>Matches <see cref="ImGuiComponents.ToggleButton"/>'s own width calc.</summary>
    private static float ToggleWidth() => ImGui.GetFrameHeight() * 1.55f;

    /// <summary>
    /// Nav sizing from actual content: the icon column fits the widest
    /// glyph (some FontAwesome icons exceed the frame height), the rail
    /// fits icon column + widest label + breathing room, plus a toggle
    /// switch's width when any tab has one (so it never collides with the
    /// widest label).
    /// </summary>
    private void ComputeNavWidths(out float iconColumnWidth, out float railWidth)
    {
        var widestIcon = 0f;
        var widestLabel = 0f;
        var hasToggle = false;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            foreach (var section in sections)
                foreach (var tab in section.Tabs)
                    widestIcon = Math.Max(widestIcon, ImGui.CalcTextSize(tab.Icon.ToIconString()).X);
        }

        foreach (var section in sections)
        {
            foreach (var tab in section.Tabs)
            {
                widestLabel = Math.Max(widestLabel, ImGui.CalcTextSize(tab.Name).X);
                hasToggle |= tab is IToggleableTab;
            }
        }

        // Pad both sides: wide glyphs draw pixels beyond their reported
        // advance, so without slack they get clipped at the cell edge.
        iconColumnWidth = widestIcon + ImGui.GetStyle().ItemInnerSpacing.X * 2f;
        railWidth = iconColumnWidth + widestLabel + 10f * ImGuiHelpers.GlobalScale;

        if (hasToggle)
            railWidth += ImGui.GetStyle().ItemInnerSpacing.X + ToggleWidth();
    }

    private void DrawNavRail(float iconColumnWidth)
    {
        // Row height grows to fit a toggle switch (taller than one text line); icon/label text
        // is then vertically centered within it. Every row gets this height uniformly, toggle
        // or not, so the rail doesn't look uneven.
        var rowHeight = ImGui.GetFrameHeight();
        var toggleWidth = ToggleWidth();
        var textOffsetY = (rowHeight - ImGui.GetTextLineHeight()) / 2f;
        var rowRightX = ImGui.GetCursorPos().X + ImGui.GetContentRegionAvail().X;

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];

            if (section.HeaderKey != null)
                ImGui.TextDisabled(Loc.Get(section.HeaderKey));
            else
                ImGui.Separator();

            for (var tabIndex = 0; tabIndex < section.Tabs.Length; tabIndex++)
            {
                var tab = section.Tabs[tabIndex];
                var chatTwoUnavailable = tab is ChatTwoTab && !plugin.ChatTwoStyles.IsConnected;

                // Not-yet-implemented pages and an unavailable Chat 2 tab stay visible but dimmed.
                using var dim = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3,
                    tab is PlaceholderTab || chatTwoUnavailable);

                // Full-width invisible selectable, then icon + label (and, for toggleable tabs,
                // a right-aligned switch) drawn on top so the whole row is clickable.
                // AllowItemOverlap lets the toggle (drawn last, on top) steal the click when
                // hovered instead of the row switching tabs. ID is index-based (not derived from
                // tab.Name) so it stays stable across a language switch.
                var cursor = ImGui.GetCursorPos();
                if (ImGui.Selectable($"##tab-{sectionIndex}-{tabIndex}", currentTab == tab,
                        ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0, rowHeight)))
                    currentTab = tab;

                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    // Centered in the icon column so glyph overflow has
                    // slack on both sides instead of clipping at the edge.
                    var glyph = tab.Icon.ToIconString();
                    var glyphOffset = (iconColumnWidth - ImGui.CalcTextSize(glyph).X) / 2f;
                    ImGui.SetCursorPos(new Vector2(cursor.X + glyphOffset, cursor.Y + textOffsetY));
                    ImGui.TextUnformatted(glyph);
                }

                ImGui.SameLine();
                ImGui.SetCursorPos(new Vector2(cursor.X + iconColumnWidth, cursor.Y + textOffsetY));
                ImGui.TextUnformatted(tab.Name);

                if (tab is IToggleableTab toggleable)
                {
                    var enabled = toggleable.Enabled;
                    ImGui.SameLine();
                    ImGui.SetCursorPos(new Vector2(rowRightX - toggleWidth, cursor.Y));
                    if (ImGuiComponents.ToggleButton($"##tab-toggle-{sectionIndex}-{tabIndex}", ref enabled))
                        toggleable.Enabled = enabled;
                }
            }
        }
    }

    private void DrawFooter()
    {
        var persist = false;

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, Loc.Get("Settings_Footer_Save")))
        {
            persist = true;
            IsOpen = false;
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, Loc.Get("Settings_Footer_Apply")))
            persist = true;

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, Loc.Get("Settings_Footer_Cancel")))
            IsOpen = false; // IsWindowAppearing re-stages on next open

#if DEBUG
        ImGui.SameLine();
        DrawChatTwoStatus();
#endif

        if (!persist)
            return;

        plugin.Configuration.UpdateFrom(mutable);
        plugin.Configuration.Save();
        plugin.ChatListener.SettingsChanged();
        plugin.ChatTwoStyles.SettingsChanged();
        plugin.RefreshLanguage();
        mutable.UpdateFrom(plugin.Configuration);
    }

#if DEBUG
    /// <summary>
    /// Right corner of the footer, debug builds only: Chat 2 styling connection state plus a
    /// Connect/Disconnect toggle. Release builds rely on the automatic connect (construction,
    /// ChatTwo.Available) and read the status from the General page's Optional plugins row; this
    /// manual override exists for testing. Acts on the live provider immediately (not staged) —
    /// a connection isn't configuration.
    /// </summary>
    private void DrawChatTwoStatus()
    {
        var styles = plugin.ChatTwoStyles;
        var connected = styles.IsConnected;
        var icon = (connected ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString();
        var buttonLabel = Loc.Get(connected ? "ChatTwo_Disconnect" : "ChatTwo_Connect");

        float iconWidth;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconWidth = ImGui.CalcTextSize(icon).X;

        var style = ImGui.GetStyle();
        var totalWidth = ImGui.CalcTextSize("Chat 2").X + style.ItemSpacing.X + iconWidth
            + style.ItemSpacing.X + ImGui.CalcTextSize(buttonLabel).X + style.FramePadding.X * 2f;
        var slack = ImGui.GetContentRegionAvail().X - totalWidth;
        if (slack > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + slack);

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Chat 2");

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(connected ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3, icon);
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
                ImGui.TextUnformatted(Loc.Get(connected ? "ChatTwo_Status_Connected" : "ChatTwo_Status_NotConnected"));
        }

        ImGui.SameLine();
        if (!ImGui.Button(buttonLabel))
            return;

        if (connected)
            styles.Disconnect();
        else
            styles.Resume();
    }
#endif
}
