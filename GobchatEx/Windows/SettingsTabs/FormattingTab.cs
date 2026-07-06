using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Mirrors the app's Formatting page: segment colors ("Color selection for
/// text types") plus the channels RP formatting applies to. Linkshells and
/// cross-world linkshells are tucked into collapsed headers so the main
/// channel grid stays scannable.
/// </summary>
internal sealed class FormattingTab : IToggleableTab
{
    private static readonly (string LabelKey, XivChatType Type)[] MainChannels =
    [
        ("Formatting_Channel_Say", XivChatType.Say),
        ("Formatting_Channel_Emote", XivChatType.CustomEmote),
        ("Formatting_Channel_StandardEmote", XivChatType.StandardEmote),
        ("Formatting_Channel_Yell", XivChatType.Yell),
        ("Formatting_Channel_Shout", XivChatType.Shout),
        ("Formatting_Channel_Party", XivChatType.Party),
        ("Formatting_Channel_CrossParty", XivChatType.CrossParty),
        ("Formatting_Channel_Alliance", XivChatType.Alliance),
        ("Formatting_Channel_FreeCompany", XivChatType.FreeCompany),
        ("Formatting_Channel_TellIn", XivChatType.TellIncoming),
        ("Formatting_Channel_TellOut", XivChatType.TellOutgoing),
        ("Formatting_Channel_NoviceNetwork", XivChatType.NoviceNetwork),
        ("Formatting_Channel_Echo", XivChatType.Echo),
    ];

    private static readonly (string LabelKey, XivChatType Type)[] LinkshellChannels =
    [
        ("Formatting_Channel_Linkshell1", XivChatType.Ls1),
        ("Formatting_Channel_Linkshell2", XivChatType.Ls2),
        ("Formatting_Channel_Linkshell3", XivChatType.Ls3),
        ("Formatting_Channel_Linkshell4", XivChatType.Ls4),
        ("Formatting_Channel_Linkshell5", XivChatType.Ls5),
        ("Formatting_Channel_Linkshell6", XivChatType.Ls6),
        ("Formatting_Channel_Linkshell7", XivChatType.Ls7),
        ("Formatting_Channel_Linkshell8", XivChatType.Ls8),
    ];

    private static readonly (string LabelKey, XivChatType Type)[] CrossworldLinkshellChannels =
    [
        ("Formatting_Channel_Cwls1", XivChatType.CrossLinkShell1),
        ("Formatting_Channel_Cwls2", XivChatType.CrossLinkShell2),
        ("Formatting_Channel_Cwls3", XivChatType.CrossLinkShell3),
        ("Formatting_Channel_Cwls4", XivChatType.CrossLinkShell4),
        ("Formatting_Channel_Cwls5", XivChatType.CrossLinkShell5),
        ("Formatting_Channel_Cwls6", XivChatType.CrossLinkShell6),
        ("Formatting_Channel_Cwls7", XivChatType.CrossLinkShell7),
        ("Formatting_Channel_Cwls8", XivChatType.CrossLinkShell8),
    ];

    public string Name => Loc.Get("Formatting_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Font;

    public bool Enabled
    {
        get => mutable.RpHighlightEnabled;
        set => mutable.RpHighlightEnabled = value;
    }

    private readonly Configuration mutable;
    private readonly UiColorPicker colorPicker = new();

    public FormattingTab(Configuration mutable)
    {
        this.mutable = mutable;
    }

    public void Draw()
    {
        SettingsUi.SectionHeader(Loc.Get("Formatting_ColorSelection_Header"),
            Loc.Get("Formatting_ColorSelection_Tooltip"));
        DrawSegmentColors();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Formatting_Channels_Header"));
        DrawChannels();
    }

    private void DrawSegmentColors()
    {
        using var table = ImRaii.Table("##segmentColors", 5, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Type"), ImGuiTableColumnFlags.WidthFixed, 90f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##segment-actions", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Color"));
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Glow"));
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Delimiters"), ImGuiTableColumnFlags.WidthStretch);

        // Punctuation examples, not translated — they're syntax, not words. Say and Emote can
        // import the color the game itself uses for their channel (pattern from Chat 2's Chat
        // colours page); the other segments have no game channel equivalent.
        DrawSegmentRow("Formatting_Segment_Say", mutable.SayStyle, "\"…\"  „…“  «…»",
            Configuration.DefaultSayForeground, UiConfigOption.ColorSay);
        DrawSegmentRow("Formatting_Segment_Emote", mutable.EmoteStyle, "*…*  <…>",
            Configuration.DefaultEmoteForeground, UiConfigOption.ColorEmoteUser);
        DrawSegmentRow("Formatting_Segment_Ooc", mutable.OocStyle, "((…))",
            Configuration.DefaultOocForeground, importOption: null);
        DrawSegmentRow("Formatting_Segment_Mention", mutable.MentionStyle, Loc.Get("Formatting_Segment_Mention_Delimiters"),
            Configuration.DefaultMentionForeground, importOption: null);
    }

    private void DrawSegmentRow(
        string labelKey, SegmentStyle style, string delimiters, ushort defaultForeground, UiConfigOption? importOption)
    {
        using var id = ImRaii.PushId(labelKey);
        var label = Loc.Get(labelKey);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var enabled = style.Enabled;
        if (ImGui.Checkbox(label, ref enabled))
            style.Enabled = enabled;

        using var disabled = ImRaii.Disabled(!style.Enabled);

        ImGui.TableNextColumn();
        using (ImRaii.PushId("reset"))
        {
            if (SettingsUi.DangerButton(FontAwesomeIcon.Undo, Loc.Get("Formatting_Reset_Tooltip")))
            {
                style.Foreground = defaultForeground;
                style.Glow = 0;
            }
        }

        if (importOption is { } option)
        {
            ImGui.SameLine();
            using (ImRaii.PushId("import"))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.LongArrowAltDown)
                    && ImportGameChannelRow(option) is { } gameRow)
                    style.Foreground = gameRow;

                DrawActionTooltip(Loc.Get("Formatting_ImportGame_Tooltip"));
            }
        }

        ImGui.TableNextColumn();
        var foreground = style.Foreground;
        if (colorPicker.Draw("fg", ref foreground, glow: false))
            style.Foreground = foreground;

        ImGui.TableNextColumn();
        var glowColor = style.Glow;
        if (colorPicker.Draw("glow", ref glowColor, glow: true))
            style.Glow = glowColor;

        ImGui.TableNextColumn();
        ImGui.TextDisabled(delimiters);
    }

    private static void DrawActionTooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        using (ImRaii.Tooltip())
            ImGui.TextUnformatted(text);
    }

    /// <summary>
    /// The game's configured color for a chat channel (Character Configuration → Log Text
    /// Color), mapped to the nearest UIColor row — SeString rewriting needs rows, not RGB.
    /// The config value's low 24 bits are RGB; 0 means "not set" (same reading as Chat 2's
    /// GetChannelColor). Null when unavailable, leaving the current color untouched.
    /// </summary>
    private static ushort? ImportGameChannelRow(UiConfigOption option)
    {
        if (!Plugin.GameConfig.TryGet(option, out uint value))
            return null;

        var rgb = value & 0xFFFFFF;
        if (rgb == 0)
            return null;

        return UiColorDimmer.NearestRow(new Vector3(
            ((rgb >> 16) & 255) / 255f,
            ((rgb >> 8) & 255) / 255f,
            (rgb & 255) / 255f));
    }

    private void DrawChannels()
    {
        DrawChannelGrid("##channels-main", MainChannels);

        if (ImGui.CollapsingHeader(Loc.Get("Formatting_Channels_Linkshells")))
            DrawChannelGrid("##channels-ls", LinkshellChannels);

        if (ImGui.CollapsingHeader(Loc.Get("Formatting_Channels_CrossworldLinkshells")))
            DrawChannelGrid("##channels-cwls", CrossworldLinkshellChannels);

        ImGuiHelpers.ScaledDummy(2f);
        if (SettingsUi.DangerButton(FontAwesomeIcon.Undo, Loc.Get("Formatting_Channels_ResetDefaults"),
                Loc.Get("Formatting_Channels_ResetDefaults_Tooltip")))
            mutable.HighlightChannels = [.. Configuration.DefaultHighlightChannels];
    }

    private void DrawChannelGrid(string id, (string LabelKey, XivChatType Type)[] choices)
    {
        using var table = ImRaii.Table(id, 3);
        if (!table)
            return;

        foreach (var (labelKey, type) in choices)
        {
            ImGui.TableNextColumn();
            var active = mutable.HighlightChannels.Contains(type);
            if (!ImGui.Checkbox(Loc.Get(labelKey), ref active))
                continue;

            if (active)
                mutable.HighlightChannels.Add(type);
            else
                mutable.HighlightChannels.Remove(type);
        }
    }
}
