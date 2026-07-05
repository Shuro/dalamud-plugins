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
    /// One nav-rail section: a header above its pages, or a divider when
    /// <paramref name="HeaderKey"/> is null (the app does this before About).
    /// </summary>
    private sealed record NavSection(string? HeaderKey, ISettingsTab[] Tabs);

    private readonly Plugin plugin;
    private readonly Configuration mutable;
    private readonly List<NavSection> sections;
    private ISettingsTab currentTab;

    public SettingsWindow(Plugin plugin)
        : base("GobchatEx Settings###GobchatExSettings",
               ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse)
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
                new GroupsTab(mutable, plugin.FriendGroups, plugin.ChatTwoStyles),
                new RangeTab(mutable, plugin.ChatTwoStyles),
                new ChatTwoTab(mutable, plugin.ChatTwoStyles),
            ]),
            new NavSection(null, [new AboutTab()]),
        ];
        currentTab = sections[0].Tabs[0];

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Heart,
            IconColor = KofiIconColor,
            IconOffset = new Vector2(1.5f, 1f),
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

    /// <summary>
    /// Nav sizing from actual content: the icon column fits the widest
    /// glyph (some FontAwesome icons exceed the frame height), the rail
    /// fits icon column + widest label + breathing room.
    /// </summary>
    private void ComputeNavWidths(out float iconColumnWidth, out float railWidth)
    {
        var widestIcon = 0f;
        var widestLabel = 0f;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            foreach (var section in sections)
                foreach (var tab in section.Tabs)
                    widestIcon = Math.Max(widestIcon, ImGui.CalcTextSize(tab.Icon.ToIconString()).X);
        }

        foreach (var section in sections)
            foreach (var tab in section.Tabs)
                widestLabel = Math.Max(widestLabel, ImGui.CalcTextSize(tab.Name).X);

        // Pad both sides: wide glyphs draw pixels beyond their reported
        // advance, so without slack they get clipped at the cell edge.
        iconColumnWidth = widestIcon + ImGui.GetStyle().ItemInnerSpacing.X * 2f;
        railWidth = iconColumnWidth + widestLabel + 10f * ImGuiHelpers.GlobalScale;
    }

    private void DrawNavRail(float iconColumnWidth)
    {
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

                // Not-yet-implemented pages stay visible but dimmed.
                using var dim = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3,
                    tab is PlaceholderTab);

                // Full-width invisible selectable, then icon + label drawn
                // on top so the whole row is clickable. ID is index-based
                // (not derived from tab.Name) so it stays stable across a
                // language switch.
                var cursor = ImGui.GetCursorPos();
                if (ImGui.Selectable($"##tab-{sectionIndex}-{tabIndex}", currentTab == tab))
                    currentTab = tab;

                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    // Centered in the icon column so glyph overflow has
                    // slack on both sides instead of clipping at the edge.
                    var glyph = tab.Icon.ToIconString();
                    var glyphOffset = (iconColumnWidth - ImGui.CalcTextSize(glyph).X) / 2f;
                    ImGui.SetCursorPos(new Vector2(cursor.X + glyphOffset, cursor.Y));
                    ImGui.TextUnformatted(glyph);
                }

                ImGui.SameLine();
                ImGui.SetCursorPosX(cursor.X + iconColumnWidth);
                ImGui.TextUnformatted(tab.Name);
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

        if (!persist)
            return;

        plugin.Configuration.UpdateFrom(mutable);
        plugin.Configuration.Save();
        plugin.ChatListener.SettingsChanged();
        plugin.ChatTwoStyles.SettingsChanged();
        plugin.RefreshLanguage();
        mutable.UpdateFrom(plugin.Configuration);
    }
}
