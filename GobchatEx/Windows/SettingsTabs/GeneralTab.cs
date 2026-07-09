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

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("General_OptionalPlugins_Header"), Loc.Get("General_OptionalPlugins_Tooltip"));
        DrawOptionalPlugins();
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
