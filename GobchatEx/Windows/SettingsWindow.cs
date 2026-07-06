using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GobchatEx.Config;
using GobchatEx.Localization;
using GobchatEx.Windows.SettingsTabs;

namespace GobchatEx.Windows;

/// <summary>
/// Settings window with a sectioned nav rail mirroring the standalone
/// GobchatEx app (General / Appearance / Chat, divider, About); pages
/// without plugin functionality yet are placeholders. Right side is the
/// page content; a Ko-fi heart sits in the title bar. Edits apply
/// instantly: tabs write straight to the live configuration and the
/// window commits (persists + applies) detected changes on a debounced
/// tick — see <see cref="CommitIfChanged"/>. There are no Save/Cancel
/// buttons; destructive actions are Ctrl+Shift-gated instead.
/// </summary>
public class SettingsWindow : Window
{
    private const string KofiUrl = "https://ko-fi.com/shuro2005";

    /// <summary>
    /// How often <see cref="Update"/> checks for configuration changes to
    /// commit. Coalesces per-frame widget edits (a dragged slider reports a
    /// change every frame) into at most two disk writes per second.
    /// </summary>
    private const long CommitDebounceMs = 500;

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
    private readonly List<NavSection> sections;
    private ISettingsTab currentTab;

    /// <summary>
    /// Per-section JSON snapshots of the configuration as of the last commit,
    /// parallel to <see cref="Configuration.Sections"/> — the change-detection
    /// baseline for <see cref="CommitIfChanged"/>.
    /// </summary>
    private string[] lastPersisted;
    private long nextCommitCheck;

    /// <summary>
    /// Requests that <see cref="Update"/> re-baseline change detection before
    /// its next commit check. Set while the window is closed (construction,
    /// OnClose): external writers (chat context menu, /gobchat group) persist
    /// and apply on their own, so their edits must not be committed again when
    /// the window opens. Handled in Update rather than OnOpen because the
    /// window host runs Update before it fires the open transition.
    /// </summary>
    private bool rebaseline = true;

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

        lastPersisted = SnapshotSections();

        sections =
        [
            new NavSection("Settings_Nav_General",
            [
                new GeneralTab(plugin.Configuration.General, plugin.ChatTwoStyles),
                new PlaceholderTab("Placeholder_Profiles_Name", FontAwesomeIcon.Users,
                    "Placeholder_Profiles_Description"),
                new PlaceholderTab("Placeholder_Logs_Name", FontAwesomeIcon.FileAlt,
                    "Placeholder_Logs_Description"),
            ]),
            new NavSection("Settings_Nav_Appearance",
            [
                new FormattingTab(plugin.Configuration.Formatting),
            ]),
            new NavSection("Settings_Nav_Chat",
            [
                new MentionsTab(plugin.Configuration.Mentions),
                new GroupsTab(plugin.Configuration.Groups, plugin.ChatTwoStyles),
                new RangeTab(plugin.Configuration.RangeFilter, plugin.ChatTwoStyles),
                new ChatTwoTab(plugin.Configuration.Tabs, plugin.ChatTwoStyles),
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
        => WindowName = $"{Loc.Get("Settings_WindowTitle")}###GobchatExSettings";

    /// <summary>
    /// Debounced instant-apply tick. Runs every frame while the window is
    /// open — even collapsed, unlike Draw — so edits still commit when the
    /// user collapses the window right after changing something.
    /// </summary>
    public override void Update()
    {
        var now = Environment.TickCount64;

        if (rebaseline)
        {
            rebaseline = false;
            lastPersisted = SnapshotSections();
            nextCommitCheck = now + CommitDebounceMs;
            return;
        }

        if (now < nextCommitCheck)
            return;

        nextCommitCheck = now + CommitDebounceMs;
        CommitIfChanged();
    }

    /// <summary>Commits an edit made within the debounce window before closing.</summary>
    public override void OnClose()
    {
        CommitIfChanged();
        rebaseline = true;
    }

    /// <summary>
    /// Instant-apply core: tabs (and the nav rail's toggles) write straight
    /// to the live configuration, and this detects those edits by comparing
    /// per-section JSON snapshots against the last-committed ones — one
    /// mechanism for every widget, no per-site change plumbing. On a change:
    /// persist only the section files that changed (reusing their snapshots),
    /// then rebuild the chat pipeline and Chat 2 styling and re-resolve the
    /// UI language once. The baseline advances even if a disk write failed
    /// (SaveSection logs and swallows I/O errors) — the in-memory apply has
    /// already happened either way.
    /// </summary>
    internal void CommitIfChanged()
    {
        var sections = plugin.Configuration.Sections;
        var changed = false;

        for (var i = 0; i < sections.Length; i++)
        {
            var json = Configuration.Serialize(sections[i].Section);
            if (json == lastPersisted[i])
                continue;

            Configuration.SaveSection(sections[i].FileName, json);
            lastPersisted[i] = json;
            changed = true;
        }

        if (!changed)
            return;

        plugin.ChatListener.SettingsChanged();
        plugin.ChatTwoStyles.SettingsChanged();
        plugin.RefreshLanguage();
    }

    /// <summary>One change-detection snapshot per section, parallel to <see cref="Configuration.Sections"/>.</summary>
    private string[] SnapshotSections()
    {
        var sections = plugin.Configuration.Sections;
        var snapshot = new string[sections.Length];
        for (var i = 0; i < sections.Length; i++)
            snapshot[i] = Configuration.Serialize(sections[i].Section);
        return snapshot;
    }

    public override void Draw()
    {
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

#if DEBUG
                // Reserve one line below the tab content for the debug-only
                // Chat 2 status row (height formula from ChatTwo's settings
                // window).
                var style = ImGui.GetStyle();
                var height = ImGui.GetContentRegionAvail().Y - style.FramePadding.Y * 2
                    - style.ItemSpacing.Y - style.ItemInnerSpacing.Y * 2 - ImGui.CalcTextSize("A").Y;
#else
                var height = ImGui.GetContentRegionAvail().Y;
#endif

                using var child = ImRaii.Child("##gobchatex-settings-tab", new Vector2(-1, height));
                if (child)
                    currentTab.Draw();
            }
        }

#if DEBUG
        ImGui.Separator();
        DrawChatTwoStatus();
#endif
    }

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
            railWidth += ImGui.GetStyle().ItemInnerSpacing.X + SettingsUi.ToggleWidth();
    }

    private void DrawNavRail(float iconColumnWidth)
    {
        // Row height grows to fit a toggle switch (taller than one text line); icon/label text
        // is then vertically centered within it. Every row gets this height uniformly, toggle
        // or not, so the rail doesn't look uneven.
        var rowHeight = ImGui.GetFrameHeight();
        var toggleWidth = SettingsUi.ToggleWidth();
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
                // hovered instead of the row switching tabs. AllowDoubleClick lets a double-click
                // anywhere else on the row flip a toggleable tab's switch too (a bigger, more
                // forgiving target than the switch itself). ID is index-based (not derived from
                // tab.Name) so it stays stable across a language switch.
                var cursor = ImGui.GetCursorPos();
                if (ImGui.Selectable($"##tab-{sectionIndex}-{tabIndex}", currentTab == tab,
                        ImGuiSelectableFlags.AllowItemOverlap | ImGuiSelectableFlags.AllowDoubleClick,
                        new Vector2(0, rowHeight)))
                {
                    if (tab is IToggleableTab doubleClickTarget && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        doubleClickTarget.Enabled = !doubleClickTarget.Enabled;
                    else
                        currentTab = tab;
                }

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
                    if (SettingsUi.ToggleSwitch($"##tab-toggle-{sectionIndex}-{tabIndex}", ref enabled))
                        toggleable.Enabled = enabled;
                }
            }
        }
    }

#if DEBUG
    /// <summary>
    /// Footer row below the tab content, debug builds only: Chat 2 styling connection state plus
    /// a Connect/Disconnect toggle. Release builds have no footer — they rely on the automatic
    /// connect (construction, ChatTwo.Available) and read the status from the General page's
    /// Optional plugins row; this manual override exists for testing. Acts on the live provider
    /// immediately without waiting for a commit tick — a connection isn't configuration.
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
