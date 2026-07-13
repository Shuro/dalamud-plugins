using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Config;
using GobchatEx.Core;
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
    // These three grids also define ChatListener.MentionSoundChannels' conversational-channel
    // universe — keep that set in sync when adding or removing a channel here. Internal because
    // ChatLogTab offers the same conversational channels in its own grids.
    internal static readonly (string LabelKey, XivChatType Type)[] MainChannels =
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

    internal static readonly (string LabelKey, XivChatType Type)[] LinkshellChannels =
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

    internal static readonly (string LabelKey, XivChatType Type)[] CrossworldLinkshellChannels =
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
        get => config.RpHighlightEnabled;
        set => config.RpHighlightEnabled = value;
    }

    private readonly FormattingConfig config;

    public FormattingTab(FormattingConfig config)
    {
        this.config = config;
    }

    public void Draw()
    {
        SettingsUi.SectionHeader(Loc.Get("Formatting_ColorSelection_Header"),
            Loc.Get("Formatting_ColorSelection_Tooltip"));
        DrawSegmentColors();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Formatting_DetectEmote_Header"));
        DrawEmoteDetection();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Formatting_Channels_Header"));
        DrawChannels();
    }

    // No UI-side disabling when the Emote segment style is off — the behavior gate lives in
    // ChatListener.SettingsChanged, same as the channel grid below, which also doesn't grey out.
    private void DrawEmoteDetection()
    {
        var detectSay = config.DetectEmoteInSay;
        if (SettingsUi.Toggle(Loc.Get("Formatting_DetectEmoteSay_Name"), ref detectSay))
            config.DetectEmoteInSay = detectSay;
        ImGuiComponents.HelpMarker(Loc.Get("Formatting_DetectEmote_Tooltip"));

        var detectParty = config.DetectEmoteInParty;
        if (SettingsUi.Toggle(Loc.Get("Formatting_DetectEmoteParty_Name"), ref detectParty))
            config.DetectEmoteInParty = detectParty;
        ImGuiComponents.HelpMarker(Loc.Get("Formatting_DetectEmote_Tooltip"));
    }

    private void DrawSegmentColors()
    {
        using var table = ImRaii.Table("##segmentColors", 6,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg);
        if (!table)
            return;

        ImGui.TableSetupColumn(Loc.Get("Groups_Friend_Column_Active"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Type"), ImGuiTableColumnFlags.WidthFixed, 90f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##segment-actions", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Color"));
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_TextGlow"));
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Delimiters"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        // Punctuation examples, not translated — they're syntax, not words. Say and Emote can
        // import the color the game itself uses for their channel (pattern from Chat 2's Chat
        // colours page); the other segments have no game channel equivalent.
        DrawSegmentRow("Formatting_Segment_Say", config.SayStyle, "\"…\"  „…“  «…»",
            FormattingConfig.DefaultSayForeground, UiConfigOption.ColorSay);
        DrawSegmentRow("Formatting_Segment_Emote", config.EmoteStyle, "*…*  <…>",
            FormattingConfig.DefaultEmoteForeground, UiConfigOption.ColorEmoteUser);
        DrawSegmentRow("Formatting_Segment_Ooc", config.OocStyle, "((…))",
            FormattingConfig.DefaultOocForeground, importOption: null);
        DrawSegmentRow("Formatting_Segment_Mention", config.MentionStyle, Loc.Get("Formatting_Segment_Mention_Delimiters"),
            FormattingConfig.DefaultMentionForeground, importOption: null);
    }

    private void DrawSegmentRow(
        string labelKey, SegmentStyle style, string delimiters, uint defaultForeground, UiConfigOption? importOption)
    {
        using var id = ImRaii.PushId(labelKey);
        var label = Loc.Get(labelKey);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var enabled = style.Enabled;
        if (SettingsUi.ToggleSwitch("##enabled", ref enabled))
            style.Enabled = enabled;

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);

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

                SettingsUi.Tooltip(Loc.Get("Formatting_ImportGame_Tooltip"));
            }
        }

        ImGui.TableNextColumn();
        var foreground = style.Foreground;
        if (SettingsUi.RgbaColorEdit("##fg", ref foreground, allowAlpha: false))
            style.Foreground = foreground;

        ImGui.TableNextColumn();
        var glowColor = style.Glow;
        if (SettingsUi.RgbaColorEdit("##glow", ref glowColor, allowAlpha: false))
            style.Glow = glowColor;

        ImGui.TableNextColumn();
        DrawDelimiterSample(style, delimiters);
    }

    /// <summary>
    /// The delimiter examples double as a live preview of the foreground color only. Glow is
    /// deliberately NOT previewed: ImGui has no text outlining, and drawlist shadow passes
    /// (both 4- and 8-direction 1px outlines were tried) turn thin glyphs like quotation
    /// marks into unreadable blobs at UI font size — the glow swatch next to the sample
    /// already shows that color. Falls back to the disabled gray while the foreground is the
    /// "no recolor" sentinel; TextColored routes through the style alpha, so ImRaii.Disabled
    /// dims the sample like every other widget in the row.
    /// </summary>
    private static void DrawDelimiterSample(SegmentStyle style, string delimiters)
    {
        if (style.Foreground == 0)
        {
            ImGui.TextDisabled(delimiters);
            return;
        }

        ImGui.TextColored(RgbaColor.ToVector4(style.Foreground), delimiters);
    }

    /// <summary>
    /// The game's configured color for a chat channel (Character Configuration → Log Text
    /// Color) — no more UIColor row snapping needed now that Text colors render via a raw
    /// macro. Null when unavailable or unset, leaving the current color untouched.
    /// </summary>
    private static uint? ImportGameChannelRow(UiConfigOption option)
        => Plugin.GameConfig.TryGet(option, out uint value) ? RgbaColor.FromGameConfigColor(value) : null;

    private void DrawChannels()
    {
        SettingsUi.ChannelGrid("##channels-main", MainChannels, config.HighlightChannels);

        if (ImGui.CollapsingHeader(Loc.Get("Formatting_Channels_Linkshells")))
            SettingsUi.ChannelGrid("##channels-ls", LinkshellChannels, config.HighlightChannels);

        if (ImGui.CollapsingHeader(Loc.Get("Formatting_Channels_CrossworldLinkshells")))
            SettingsUi.ChannelGrid("##channels-cwls", CrossworldLinkshellChannels, config.HighlightChannels);
    }
}
