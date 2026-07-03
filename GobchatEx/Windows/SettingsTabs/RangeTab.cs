using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Range filter settings (Milestone 3): fade chat from far-away players into darker color steps,
/// hide it beyond the cut-off. The two distance sliders keep fade-start ≤ cut-off between them —
/// equal values are allowed and mean a hard cutoff with no fade ramp.
/// </summary>
internal sealed class RangeTab : ISettingsTab
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

    private readonly Configuration mutable;

    public RangeTab(Configuration mutable)
    {
        this.mutable = mutable;
    }

    public void Draw()
    {
        var enabled = mutable.RangeFilterEnabled;
        if (SettingsUi.Toggle(Loc.Get("Range_Enable_Name"), ref enabled))
            mutable.RangeFilterEnabled = enabled;
        ImGuiComponents.HelpMarker(Loc.Get("Range_Enable_Tooltip"));

        ImGuiHelpers.ScaledDummy(6f);
        using var disabled = ImRaii.Disabled(!mutable.RangeFilterEnabled);

        DrawDistanceSliders();

        ImGuiHelpers.ScaledDummy(6f);
        var mentionsIgnore = mutable.RangeFilterMentionsIgnoreRange;
        if (SettingsUi.Toggle(Loc.Get("Range_MentionsIgnore_Name"), ref mentionsIgnore))
            mutable.RangeFilterMentionsIgnoreRange = mentionsIgnore;
        ImGuiComponents.HelpMarker(Loc.Get("Range_MentionsIgnore_Tooltip"));

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("Range_Channels_Header"), Loc.Get("Range_Channels_Tooltip"));
        DrawChannels();
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
