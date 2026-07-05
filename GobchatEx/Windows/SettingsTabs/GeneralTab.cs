using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

internal sealed class GeneralTab : ISettingsTab
{
    public string Name => Loc.Get("General_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Cog;

    private readonly Configuration mutable;
    private readonly ChatTwoStyleProvider chatTwoStyles;

    public GeneralTab(Configuration mutable, ChatTwoStyleProvider chatTwoStyles)
    {
        this.mutable = mutable;
        this.chatTwoStyles = chatTwoStyles;
    }

    public void Draw()
    {
        var movable = mutable.IsConfigWindowMovable;
        if (SettingsUi.Toggle(Loc.Get("General_MovableWindow_Name"), ref movable))
            mutable.IsConfigWindowMovable = movable;
        ImGuiComponents.HelpMarker(Loc.Get("General_MovableWindow_Tooltip"));

        ImGuiHelpers.ScaledDummy(6f);
        DrawLanguage();

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("General_OptionalPlugins_Header"), Loc.Get("General_OptionalPlugins_Tooltip"));
        DrawOptionalPlugins();
    }

    /// <summary>
    /// One row per optional plugin GEX integrates with, with a live ✓/✗ for whether the
    /// integration is usable right now. ✓ means the styling IPC is actually connected — a
    /// Chat 2 without message-styling support shows ✗, distinguished in the tooltip.
    /// </summary>
    private void DrawOptionalPlugins()
    {
        var connected = chatTwoStyles.IsConnected;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Chat 2");

        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(connected ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3,
                (connected ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString());
        }

        if (!ImGui.IsItemHovered())
            return;

        using (ImRaii.Tooltip())
            ImGui.TextUnformatted(ChatTwoTooltip(connected));
    }

    private static string ChatTwoTooltip(bool connected)
    {
        if (connected)
            return Loc.Get("ChatTwo_Status_Connected");

        var loaded = Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "ChatTwo" && p.IsLoaded);
        return Loc.Get(loaded ? "General_ChatTwo_NoStyling" : "General_ChatTwo_NotInstalled");
    }

    private void DrawLanguage()
    {
        ImGui.TextUnformatted(Loc.Get("General_Language_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("General_Language_Tooltip"));

        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("##language", mutable.LanguageOverride.Name()))
        {
            if (combo)
            {
                foreach (var option in Enum.GetValues<LanguageOverride>())
                {
                    if (ImGui.Selectable(option.Name(), option == mutable.LanguageOverride))
                        mutable.LanguageOverride = option;
                }
            }
        }
    }
}
