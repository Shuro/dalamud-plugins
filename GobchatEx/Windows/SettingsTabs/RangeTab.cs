using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Range filter settings (Milestone 3): fade chat from far-away players into darker color steps,
/// hide it beyond the cut-off. The two distance sliders keep fade-start ≤ cut-off between them —
/// equal values are allowed and mean a hard cutoff with no fade ramp.
/// </summary>
internal sealed class RangeTab : IToggleableTab
{
    private const float MaxDistanceYalms = 100f;

    // The cut-off slider only drags up to here — a shorter range keeps the useful low end
    // precise. Ctrl+click the slider to type any higher value: ImGui only clamps typed input
    // when AlwaysClamp is set, which it isn't here.
    private const float MaxCutOffSliderYalms = 60f;

    // Only proximity channels are offered: range-filtering a server-wide channel (party, FC,
    // linkshells) would hide messages based on where the sender happens to be standing.
    // Say/Emote carry an info marker: the game engine only delivers them up to ~20 yalms,
    // so distances configured beyond that never see a message on these channels.
    private static readonly (string LabelKey, XivChatType Type, bool EngineLimited)[] Channels =
    [
        ("Formatting_Channel_Say", XivChatType.Say, true),
        ("Formatting_Channel_Emote", XivChatType.CustomEmote, true),
        ("Formatting_Channel_StandardEmote", XivChatType.StandardEmote, false),
        ("Formatting_Channel_Yell", XivChatType.Yell, false),
        ("Formatting_Channel_Shout", XivChatType.Shout, false),
    ];

    public string Name => Loc.Get("Range_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Ruler;

    public bool Enabled
    {
        get => config.RangeFilterEnabled;
        set => config.RangeFilterEnabled = value;
    }

    private readonly RangeFilterConfig config;
    private readonly ChatTwoStyleProvider chatTwoStyles;

    public RangeTab(RangeFilterConfig config, ChatTwoStyleProvider chatTwoStyles)
    {
        this.config = config;
        this.chatTwoStyles = chatTwoStyles;
    }

    public void Draw()
    {
        DrawDistanceSliders();

        ImGuiHelpers.ScaledDummy(6f);
        var mentionsIgnore = config.RangeFilterMentionsIgnoreRange;
        if (SettingsUi.Toggle(Loc.Get("Range_MentionsIgnore_Name"), ref mentionsIgnore))
            config.RangeFilterMentionsIgnoreRange = mentionsIgnore;
        ImGuiComponents.HelpMarker(Loc.Get("Range_MentionsIgnore_Tooltip"));

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("Range_Channels_Header"), Loc.Get("Range_Channels_Tooltip"));
        DrawChannels();

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("Range_ChatTwo_Header"), Loc.Get("Range_ChatTwo_Header_Tooltip"));
        DrawChatTwoOptions();
    }

    /// <summary>
    /// Chat 2 gets true per-message alpha and render-only hiding through the styling IPC; the
    /// native log always keeps the darkened-step "lite" dimming above. Disabled with a hint while
    /// the IPC isn't connected (and, via the caller's scope, while the range filter is off).
    /// </summary>
    private void DrawChatTwoOptions()
    {
        if (!chatTwoStyles.IsConnected)
            ImGui.TextDisabled(Loc.Get("ChatTwo_NotConnected_Hint"));

        using var disabled = ImRaii.Disabled(!chatTwoStyles.IsConnected);

        var fade = config.RangeFilterChatTwoFade;
        if (SettingsUi.Toggle(Loc.Get("Range_ChatTwo_Fade_Name"), ref fade))
            config.RangeFilterChatTwoFade = fade;
        ImGuiComponents.HelpMarker(Loc.Get("Range_ChatTwo_Fade_Tooltip"));

        var hide = config.RangeFilterChatTwoHide;
        if (SettingsUi.Toggle(Loc.Get("Range_ChatTwo_Hide_Name"), ref hide))
            config.RangeFilterChatTwoHide = hide;
        ImGuiComponents.HelpMarker(Loc.Get("Range_ChatTwo_Hide_Tooltip"));
    }

    private void DrawDistanceSliders()
    {
        ImGui.TextUnformatted(Loc.Get("Range_FadeOut_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_FadeOut_Tooltip"));
        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        var fadeOut = config.RangeFilterFadeOut;
        if (ImGui.SliderFloat("##range-fadeout", ref fadeOut, 0f, MaxDistanceYalms, "%.0f"))
            config.RangeFilterFadeOut = Math.Min(fadeOut, config.RangeFilterCutOff);

        ImGui.TextUnformatted(Loc.Get("Range_CutOff_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_CutOff_Tooltip"));
        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        var cutOff = config.RangeFilterCutOff;
        if (ImGui.SliderFloat("##range-cutoff", ref cutOff, 0f, MaxCutOffSliderYalms, "%.0f"))
        {
            config.RangeFilterCutOff = cutOff;
            config.RangeFilterFadeOut = Math.Min(config.RangeFilterFadeOut, cutOff);
        }
    }

    private void DrawChannels()
    {
        using var table = ImRaii.Table("##range-channels", 3);
        if (!table)
            return;

        foreach (var (labelKey, type, engineLimited) in Channels)
        {
            ImGui.TableNextColumn();
            var active = config.RangeFilterChannels.Contains(type);
            var changed = ImGui.Checkbox(Loc.Get(labelKey), ref active);
            if (engineLimited)
                ImGuiComponents.HelpMarker(Loc.Get("Range_EngineLimit_Tooltip"));

            if (!changed)
                continue;

            if (active)
                config.RangeFilterChannels.Add(type);
            else
                config.RangeFilterChannels.Remove(type);
        }
    }
}
