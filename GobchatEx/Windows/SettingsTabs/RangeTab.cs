using System;
using Dalamud.Bindings.ImGui;
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

    public string Name => Loc.Get("Range_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Ruler;

    public bool Enabled
    {
        get => config.RangeFilterEnabled;
        set => config.RangeFilterEnabled = value;
    }

    private readonly RangeFilterConfig config;
    private readonly ChatTwoStyleProvider chatTwoStyles;
    private readonly RangeRingsOverlay rangeRings;

    public RangeTab(RangeFilterConfig config, ChatTwoStyleProvider chatTwoStyles, RangeRingsOverlay rangeRings)
    {
        this.config = config;
        this.chatTwoStyles = chatTwoStyles;
        this.rangeRings = rangeRings;
    }

    public void Draw()
    {
        DrawDistanceSliders();
        DrawPreviewButton();

        ImGuiHelpers.ScaledDummy(6f);
        var mentionsIgnore = config.RangeFilterMentionsIgnoreRange;
        if (SettingsUi.Toggle(Loc.Get("Range_MentionsIgnore_Name"), ref mentionsIgnore))
            config.RangeFilterMentionsIgnoreRange = mentionsIgnore;
        ImGuiComponents.HelpMarker(Loc.Get("Range_MentionsIgnore_Tooltip"));

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("Range_Channels_Header"), Loc.Get("Range_Channels_Tooltip"));
        SettingsUi.ChannelGrid("##range-channels", ChatChannels.Range, config.RangeFilterChannels);

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("Range_ChatTwo_Header"), Loc.Get("Range_ChatTwo_Header_Tooltip"));
        DrawChatTwoOptions();
    }

    /// <summary>
    /// The fade curve's start/end opacity for Chat 2's true per-message alpha (app parity:
    /// opacity steps from 100% down to the start value the moment fading begins, then ramps to
    /// the end value at the cut-off before render-only hiding). Disabled without a styling
    /// connection because only Chat 2 can render per-message alpha — the native log keeps its
    /// darkened-step "lite" dimming above regardless.
    /// </summary>
    private void DrawChatTwoOptions()
    {
        if (!chatTwoStyles.IsConnected)
            SettingsUi.Warning(Loc.Get("ChatTwo_NotConnected_Hint"));

        using var disabled = ImRaii.Disabled(!chatTwoStyles.IsConnected);
        var sliderWidth = MathF.Min(ImGui.GetContentRegionAvail().X, 480f * ImGuiHelpers.GlobalScale);

        // AlwaysClamp deliberately, unlike the distance sliders (where typing past the slider max
        // is a feature): an opacity outside 1-100% is never meaningful. No start >= end coupling
        // either — the app doesn't enforce it and the lerp is well-defined inverted.
        ImGui.TextUnformatted(Loc.Get("Range_StartOpacity_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_StartOpacity_Tooltip"));
        ImGui.SetNextItemWidth(sliderWidth);
        var startOpacity = config.RangeFilterStartOpacity;
        if (ImGui.SliderInt("##range-start-opacity", ref startOpacity, 1, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp))
            config.RangeFilterStartOpacity = startOpacity;

        ImGui.TextUnformatted(Loc.Get("Range_EndOpacity_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_EndOpacity_Tooltip"));
        ImGui.SetNextItemWidth(sliderWidth);
        var endOpacity = config.RangeFilterEndOpacity;
        if (ImGui.SliderInt("##range-end-opacity", ref endOpacity, 1, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp))
            config.RangeFilterEndOpacity = endOpacity;
    }

    /// <summary>
    /// Fires the transient in-game preview (RangeRingsOverlay): ground rings at the two slider
    /// distances for ~8 seconds. Radii are read at click time — the sliders above edit the live
    /// config, so a re-click after dragging previews the new values. Needs a loaded character to
    /// have something to draw around (mirrors MentionsTab's add-current-character gate).
    /// </summary>
    private void DrawPreviewButton()
    {
        ImGuiHelpers.ScaledDummy(2f);
        using (ImRaii.Disabled(!Plugin.PlayerState.IsLoaded))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Bullseye, Loc.Get("Range_Preview_Button")))
                rangeRings.Show(config.RangeFilterFadeOut, config.RangeFilterCutOff);
        }
        SettingsUi.Tooltip(Loc.Get("Range_Preview_Tooltip"));
    }

    private void DrawDistanceSliders()
    {
        // Fill the row at the minimum window size, but cap the stretch — a monitor-wide
        // slider for a 0-100 yalm range adds no precision (Ctrl+click types exact values).
        var sliderWidth = MathF.Min(ImGui.GetContentRegionAvail().X, 480f * ImGuiHelpers.GlobalScale);

        ImGui.TextUnformatted(Loc.Get("Range_FadeOut_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_FadeOut_Tooltip"));
        ImGui.SetNextItemWidth(sliderWidth);
        var fadeOut = config.RangeFilterFadeOut;
        if (ImGui.SliderFloat("##range-fadeout", ref fadeOut, 0f, MaxDistanceYalms, "%.0f"))
            config.RangeFilterFadeOut = Math.Min(fadeOut, config.RangeFilterCutOff);

        ImGui.TextUnformatted(Loc.Get("Range_CutOff_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_CutOff_Tooltip"));
        ImGui.SetNextItemWidth(sliderWidth);
        var cutOff = config.RangeFilterCutOff;
        if (ImGui.SliderFloat("##range-cutoff", ref cutOff, 0f, MaxCutOffSliderYalms, "%.0f"))
        {
            config.RangeFilterCutOff = cutOff;
            config.RangeFilterFadeOut = Math.Min(config.RangeFilterFadeOut, cutOff);
        }
    }
}
