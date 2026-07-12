using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

internal sealed class GeneralTab : ISettingsTab
{
    public string Name => Loc.Get("General_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Cog;

    /// <summary>
    /// Refresh cadence of the installed-plugin scan behind the Chat 2 status row: a linear walk
    /// of InstalledPlugins doesn't belong in every frame, and load state doesn't change faster.
    /// </summary>
    private const int PluginScanIntervalMs = 1000;

    private readonly GeneralConfig config;
    private readonly ChatTwoStyleProvider chatTwoStyles;

    private long nextPluginScan;
    private bool chatTwoLoaded;

    public GeneralTab(GeneralConfig config, ChatTwoStyleProvider chatTwoStyles)
    {
        this.config = config;
        this.chatTwoStyles = chatTwoStyles;
    }

    public void Draw()
    {
        DrawLanguage();

        ImGuiHelpers.ScaledDummy(6f);
        var showQuickbar = config.ShowQuickbar;
        if (SettingsUi.Toggle(Loc.Get("General_ShowQuickbar_Name"), ref showQuickbar))
            config.ShowQuickbar = showQuickbar;
        ImGuiComponents.HelpMarker(Loc.Get("General_ShowQuickbar_Tooltip"));

        if (config.ShowQuickbar)
        {
            DrawQuickbarAttachOption();
            DrawQuickbarHideOptions();
        }

        ImGuiHelpers.ScaledDummy(10f);
        var legacyEchoFallback = config.LegacyEchoCommandFallback;
        if (SettingsUi.Toggle(Loc.Get("General_LegacyEchoFallback_Name"), ref legacyEchoFallback))
            config.LegacyEchoCommandFallback = legacyEchoFallback;
        ImGuiComponents.HelpMarker(Loc.Get("General_LegacyEchoFallback_Tooltip"));

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("General_OptionalPlugins_Header"), Loc.Get("General_OptionalPlugins_Tooltip"));
        DrawOptionalPlugins();
    }

    /// <summary>
    /// Placement sub-option, indented under the "Show Quickbar" toggle above
    /// the hide conditions: glues the bar to the top edge of the chat window
    /// (Chat 2 when present, otherwise the game's own chat log).
    /// </summary>
    private void DrawQuickbarAttachOption()
    {
        using var indent = ImRaii.PushIndent();
        var attach = config.QuickbarAttachToChat;
        if (SettingsUi.Toggle(Loc.Get("Quickbar_AttachToChat_Name"), ref attach))
            config.QuickbarAttachToChat = attach;
        ImGuiComponents.HelpMarker(Loc.Get("Quickbar_AttachToChat_Tooltip"));
    }

    /// <summary>
    /// Sub-options controlling when the Quickbar hides, indented under the
    /// "Show Quickbar" toggle and only visible while it's on. Names and
    /// semantics mirror Chat 2's display options; defaults are all-on.
    /// Flows two columns when the widest label fits twice (English does at
    /// the minimum window width, German falls back to one column) — measured
    /// per frame like the nav rail, so language switches just work.
    /// </summary>
    private void DrawQuickbarHideOptions()
    {
        using var indent = ImRaii.PushIndent();

        (string NameKey, string TooltipKey, bool Value, Action<bool> Set)[] options =
        [
            ("Quickbar_HideDuringCutscenes_Name", "Quickbar_HideDuringCutscenes_Tooltip",
                config.QuickbarHideDuringCutscenes, v => config.QuickbarHideDuringCutscenes = v),
            ("Quickbar_HideWhenNotLoggedIn_Name", "Quickbar_HideWhenNotLoggedIn_Tooltip",
                config.QuickbarHideWhenNotLoggedIn, v => config.QuickbarHideWhenNotLoggedIn = v),
            ("Quickbar_HideWhenUiHidden_Name", "Quickbar_HideWhenUiHidden_Tooltip",
                config.QuickbarHideWhenUiHidden, v => config.QuickbarHideWhenUiHidden = v),
            ("Quickbar_HideInLoadingScreens_Name", "Quickbar_HideInLoadingScreens_Tooltip",
                config.QuickbarHideInLoadingScreens, v => config.QuickbarHideInLoadingScreens = v),
            ("Quickbar_HideInBattle_Name", "Quickbar_HideInBattle_Tooltip",
                config.QuickbarHideInBattle, v => config.QuickbarHideInBattle = v),
            ("Quickbar_HideWhenChatHidden_Name", "Quickbar_HideWhenChatHidden_Tooltip",
                config.QuickbarHideWhenChatHidden, v => config.QuickbarHideWhenChatHidden = v),
        ];

        var style = ImGui.GetStyle();
        var widestLabel = 0f;
        foreach (var option in options)
            widestLabel = MathF.Max(widestLabel, ImGui.CalcTextSize(Loc.Get(option.NameKey)).X);

        float helpWidth;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            helpWidth = ImGui.CalcTextSize(FontAwesomeIcon.InfoCircle.ToIconString()).X;

        var itemWidth = SettingsUi.ToggleWidth() + style.ItemSpacing.X + widestLabel
            + style.ItemSpacing.X + helpWidth;
        var columns = ImGui.GetContentRegionAvail().X >= itemWidth * 2f + style.CellPadding.X * 4f ? 2 : 1;

        using var table = ImRaii.Table("##quickbar-hide", columns);
        if (!table)
            return;

        foreach (var (nameKey, tooltipKey, value, set) in options)
        {
            ImGui.TableNextColumn();
            var current = value;
            if (SettingsUi.Toggle(Loc.Get(nameKey), ref current))
                set(current);
            ImGuiComponents.HelpMarker(Loc.Get(tooltipKey));
        }
    }

    /// <summary>
    /// One row per optional plugin GEX integrates with, with a live tri-state glyph for whether
    /// the integration is usable right now: green check (styling IPC connected), yellow question
    /// mark (Chat 2 loaded, but no styling IPC — older/incompatible version), or red cross (Chat 2
    /// not installed or not loaded). Distinguished further in the tooltip.
    /// </summary>
    private void DrawOptionalPlugins()
    {
        var connected = chatTwoStyles.IsConnected;
        var now = Environment.TickCount64;
        if (now >= nextPluginScan)
        {
            nextPluginScan = now + PluginScanIntervalMs;
            chatTwoLoaded = ChatTwoStyleProvider.IsChatTwoLoaded();
        }

        var loaded = connected || chatTwoLoaded;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Chat 2");

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.AlignTextToFramePadding();
            if (connected)
                ImGui.TextColored(ImGuiColors.HealerGreen, FontAwesomeIcon.Check.ToIconString());
            else if (loaded)
                ImGui.TextColored(ImGuiColors.DalamudOrange, FontAwesomeIcon.Question.ToIconString());
            else
                ImGui.TextColored(ImGuiColors.DalamudRed, FontAwesomeIcon.Times.ToIconString());
        }

        if (!ImGui.IsItemHovered())
            return;

        using (ImRaii.Tooltip())
            ImGui.TextUnformatted(ChatTwoTooltip(connected, loaded));
    }

    private static string ChatTwoTooltip(bool connected, bool loaded)
    {
        if (connected)
            return Loc.Get("ChatTwo_Status_Connected");

        return Loc.Get(loaded ? "General_ChatTwo_NoStyling" : "General_ChatTwo_NotInstalled");
    }

    private void DrawLanguage()
    {
        ImGui.TextUnformatted(Loc.Get("General_Language_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("General_Language_Tooltip"));

        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("##language", config.LanguageOverride.Name()))
        {
            if (combo)
            {
                foreach (var option in Enum.GetValues<LanguageOverride>())
                {
                    if (ImGui.Selectable(option.Name(), option == config.LanguageOverride))
                        config.LanguageOverride = option;
                }
            }
        }
    }
}
