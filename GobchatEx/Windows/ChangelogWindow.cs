using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Windows;

/// <summary>
/// "What's new" popup, ported from OtterGui's Changelog widget
/// (github.com/Ottermandias/OtterGui, Widgets/Changelog.cs). Auto-opens via
/// PreOpenCheck when Configuration.General.ChangelogLastSeenVersion is behind
/// SeedEntries' entry list and ChangelogDisplayType allows it; AboutTab's "View
/// Changelog" button forces it open on demand via <see cref="ForceOpen"/>.
///
/// Persists its own two config fields immediately (Configuration.SaveSection)
/// instead of going through SettingsWindow's debounced commit — the same
/// "external writer" pattern SettingsWindow's `rebaseline` field documents for
/// the chat context-menu group actions, so the seen-watermark survives even if
/// the settings window is never opened.
/// </summary>
internal sealed class ChangelogWindow : Window
{
    public const int FreshInstallVersion = int.MaxValue;

    private readonly Plugin plugin;
    private readonly List<(string Title, List<Entry> Items, bool HasHighlight)> entries = new();

    private int lastSeenVersion;
    private ChangelogDisplayType displayType;

    /// <summary>Set to force the window open regardless of the seen watermark — the manual
    /// "View Changelog" entry point. Cleared once the user dismisses it.</summary>
    public bool ForceOpen { get; set; }

    public ChangelogWindow(Plugin plugin)
        : base("###GobchatExChangelog", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        DisableWindowSounds = true;

        SeedEntries();
    }

    public override void PreOpenCheck()
    {
        var general = plugin.Configuration.General;
        lastSeenVersion = general.ChangelogLastSeenVersion;
        displayType = general.ChangelogDisplayType;

        if (ForceOpen)
        {
            IsOpen = true;
            return;
        }

        if (lastSeenVersion == FreshInstallVersion)
        {
            IsOpen = false;
            Persist(entries.Count, displayType);
            return;
        }

        switch (displayType)
        {
            case ChangelogDisplayType.New:
                IsOpen = lastSeenVersion < entries.Count;
                break;
            case ChangelogDisplayType.HighlightOnly:
                IsOpen = false;
                for (var i = lastSeenVersion; i < entries.Count; i++)
                {
                    if (!entries[i].HasHighlight)
                        continue;
                    IsOpen = true;
                    break;
                }

                if (!IsOpen && lastSeenVersion < entries.Count)
                    Persist(entries.Count, displayType);
                break;
            case ChangelogDisplayType.Never:
                IsOpen = false;
                if (lastSeenVersion < entries.Count)
                    Persist(entries.Count, ChangelogDisplayType.Never);
                break;
        }
    }

    public override void PreDraw()
    {
        WindowName = $"{Loc.Get("Changelog_WindowTitle")}###GobchatExChangelog";
        Size = new Vector2(Math.Min(ImGui.GetMainViewport().Size.X / ImGuiHelpers.GlobalScale / 2, 800),
            ImGui.GetMainViewport().Size.Y / ImGuiHelpers.GlobalScale / 2);
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(
            (ImGui.GetMainViewport().Size - Size.Value * ImGuiHelpers.GlobalScale) / 2, ImGuiCond.Appearing);
    }

    public override void Draw()
    {
        DrawEntries();

        var buttonWidth = Size!.Value.X * ImGuiHelpers.GlobalScale / 3;
        ImGui.SetCursorPosX(buttonWidth);
        DrawDisplayTypeCombo(buttonWidth);
        ImGui.SetCursorPosX(buttonWidth);
        DrawUnderstoodButton(buttonWidth);
    }

    private void DrawEntries()
    {
        using var child = ImRaii.Child("##changelog-entries", new Vector2(-1, -ImGui.GetFrameHeight() * 3));
        if (!child)
            return;

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var (title, items, hasHighlight) = entries[i];
            if (title.Length == 0)
                continue;

            using var id = ImRaii.PushId(i);
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            var flags = ImGuiTreeNodeFlags.NoTreePushOnOpen;

            var isOpen = i == entries.Count - 1
                ? i == lastSeenVersion || displayType != ChangelogDisplayType.HighlightOnly || hasHighlight
                : i >= lastSeenVersion && (hasHighlight || displayType != ChangelogDisplayType.HighlightOnly);

            if (isOpen)
                flags |= ImGuiTreeNodeFlags.DefaultOpen;

            var tree = ImGui.TreeNodeEx(title, flags);
            color.Pop();
            if (!tree)
                continue;

            foreach (var entry in items)
                entry.Draw();
        }
    }

    private void DrawDisplayTypeCombo(float width)
    {
        ImGui.SetNextItemWidth(width);
        using var combo = ImRaii.Combo("##changelog-display-type", displayType.Name());
        if (!combo)
            return;

        foreach (var type in Enum.GetValues<ChangelogDisplayType>())
        {
            if (ImGui.Selectable(type.Name()))
                Persist(lastSeenVersion, type);
        }
    }

    private void DrawUnderstoodButton(float width)
    {
        if (!ImGui.Button(Loc.Get("Changelog_Understood"), new Vector2(width, 0)))
            return;

        if (lastSeenVersion != entries.Count)
            Persist(entries.Count, displayType);
        ForceOpen = false;
    }

    private void Persist(int version, ChangelogDisplayType type)
    {
        lastSeenVersion = version;
        displayType = type;

        var general = plugin.Configuration.General;
        general.ChangelogLastSeenVersion = version;
        general.ChangelogDisplayType = type;
        Configuration.SaveSection("general.json", Configuration.Serialize(general));

        // SettingsWindow may be open at the same time (its own AboutTab is what triggers this
        // window) and would otherwise see General's JSON diverge from its debounced-commit
        // baseline on its next tick and needlessly redo this write plus the full SettingsChanged
        // cascade. Tell it to resync instead.
        plugin.RebaselineSettingsWindow();
    }

    private ChangelogWindow NextVersion(string title)
    {
        entries.Add((title, new List<Entry>(), false));
        return this;
    }

    private ChangelogWindow RegisterEntry(string text)
    {
        entries[^1].Items.Add(new Entry(text, false));
        return this;
    }

    private ChangelogWindow RegisterImportant(string text)
    {
        entries[^1].Items.Add(new Entry(text, true));
        var (title, items, _) = entries[^1];
        entries[^1] = (title, items, true);
        return this;
    }

    /// <summary>
    /// Changelog content — hand-authored per release, oldest first. Add a new
    /// NextVersion(...) block when cutting a release, and rename "Unreleased" to the
    /// real version string as part of that release's version-bump commit.
    /// </summary>
    private void SeedEntries()
    {
        NextVersion("v1.0.0")
            .RegisterImportant(Loc.Get("Changelog_V1_0_0_Headline"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_ChatHighlighting"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_PlayerMentions"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_PlayerGroups"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_RangeFilter"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_ChatLogging"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_MentionSounds"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_GroupSounds"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_MentionHistory"))
            .RegisterEntry(Loc.Get("Changelog_V1_0_0_ChatCommands"));
    }

    private readonly record struct Entry(string Text, bool Highlight)
    {
        public void Draw()
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.TankBlue, Highlight);
            ImGui.Bullet();
            ImGui.SameLine();
            using (ImRaii.TextWrapPos(0f))
                ImGui.TextUnformatted(Text);
        }
    }
}
