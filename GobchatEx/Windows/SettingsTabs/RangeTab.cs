using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
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

    // Only proximity channels are offered: range-filtering a server-wide channel (party, FC,
    // linkshells) would hide messages based on where the sender happens to be standing.
    private static readonly (string LabelKey, XivChatType Type)[] Channels =
    [
        ("Formatting_Channel_Say", XivChatType.Say),
        ("Formatting_Channel_Emote", XivChatType.CustomEmote),
        ("Formatting_Channel_StandardEmote", XivChatType.StandardEmote),
        ("Formatting_Channel_Yell", XivChatType.Yell),
        ("Formatting_Channel_Shout", XivChatType.Shout),
    ];

    public string Name => Loc.Get("Range_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Ruler;

    public bool Enabled
    {
        get => mutable.RangeFilterEnabled;
        set => mutable.RangeFilterEnabled = value;
    }

    private readonly Configuration mutable;
    private readonly ChatTwoStyleProvider chatTwoStyles;

    public RangeTab(Configuration mutable, ChatTwoStyleProvider chatTwoStyles)
    {
        this.mutable = mutable;
        this.chatTwoStyles = chatTwoStyles;
    }

    public void Draw()
    {
        DrawDistanceSliders();

        ImGuiHelpers.ScaledDummy(6f);
        var mentionsIgnore = mutable.RangeFilterMentionsIgnoreRange;
        if (SettingsUi.Toggle(Loc.Get("Range_MentionsIgnore_Name"), ref mentionsIgnore))
            mutable.RangeFilterMentionsIgnoreRange = mentionsIgnore;
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

        var fade = mutable.RangeFilterChatTwoFade;
        if (SettingsUi.Toggle(Loc.Get("Range_ChatTwo_Fade_Name"), ref fade))
            mutable.RangeFilterChatTwoFade = fade;
        ImGuiComponents.HelpMarker(Loc.Get("Range_ChatTwo_Fade_Tooltip"));

        var hide = mutable.RangeFilterChatTwoHide;
        if (SettingsUi.Toggle(Loc.Get("Range_ChatTwo_Hide_Name"), ref hide))
            mutable.RangeFilterChatTwoHide = hide;
        ImGuiComponents.HelpMarker(Loc.Get("Range_ChatTwo_Hide_Tooltip"));
    }

    private void DrawDistanceSliders()
    {
        ImGui.TextUnformatted(Loc.Get("Range_FadeOut_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_FadeOut_Tooltip"));
        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        var fadeOut = mutable.RangeFilterFadeOut;
        if (ImGui.SliderFloat("##range-fadeout", ref fadeOut, 0f, MaxDistanceYalms, "%.0f"))
            mutable.RangeFilterFadeOut = Math.Min(fadeOut, mutable.RangeFilterCutOff);

        ImGui.TextUnformatted(Loc.Get("Range_CutOff_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("Range_CutOff_Tooltip"));
        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        var cutOff = mutable.RangeFilterCutOff;
        if (ImGui.SliderFloat("##range-cutoff", ref cutOff, 0f, MaxDistanceYalms, "%.0f"))
        {
            mutable.RangeFilterCutOff = cutOff;
            mutable.RangeFilterFadeOut = Math.Min(mutable.RangeFilterFadeOut, cutOff);
        }
    }

    private void DrawChannels()
    {
        using var table = ImRaii.Table("##range-channels", 3);
        if (!table)
            return;

        foreach (var (labelKey, type) in Channels)
        {
            ImGui.TableNextColumn();
            var active = mutable.RangeFilterChannels.Contains(type);
            if (!ImGui.Checkbox(Loc.Get(labelKey), ref active))
                continue;

            if (active)
                mutable.RangeFilterChannels.Add(type);
            else
                mutable.RangeFilterChannels.Remove(type);
        }
    }
}
