using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Mirrors the app's Formatting page: segment colors ("Color selection for
/// text types") plus the channels RP formatting applies to. Linkshells and
/// cross-world linkshells are tucked into collapsed headers so the main
/// channel grid stays scannable.
/// </summary>
internal sealed class FormattingTab : ISettingsTab
{
    private static readonly (string Label, XivChatType Type)[] MainChannels =
    [
        ("Say", XivChatType.Say),
        ("Emote", XivChatType.CustomEmote),
        ("Standard Emote", XivChatType.StandardEmote),
        ("Yell", XivChatType.Yell),
        ("Shout", XivChatType.Shout),
        ("Party", XivChatType.Party),
        ("Cross Party", XivChatType.CrossParty),
        ("Alliance", XivChatType.Alliance),
        ("Free Company", XivChatType.FreeCompany),
        ("Tell (in)", XivChatType.TellIncoming),
        ("Tell (out)", XivChatType.TellOutgoing),
        ("Novice Network", XivChatType.NoviceNetwork),
        ("Echo", XivChatType.Echo),
    ];

    private static readonly (string Label, XivChatType Type)[] LinkshellChannels =
    [
        ("Linkshell 1", XivChatType.Ls1),
        ("Linkshell 2", XivChatType.Ls2),
        ("Linkshell 3", XivChatType.Ls3),
        ("Linkshell 4", XivChatType.Ls4),
        ("Linkshell 5", XivChatType.Ls5),
        ("Linkshell 6", XivChatType.Ls6),
        ("Linkshell 7", XivChatType.Ls7),
        ("Linkshell 8", XivChatType.Ls8),
    ];

    private static readonly (string Label, XivChatType Type)[] CrossworldLinkshellChannels =
    [
        ("CWLS 1", XivChatType.CrossLinkShell1),
        ("CWLS 2", XivChatType.CrossLinkShell2),
        ("CWLS 3", XivChatType.CrossLinkShell3),
        ("CWLS 4", XivChatType.CrossLinkShell4),
        ("CWLS 5", XivChatType.CrossLinkShell5),
        ("CWLS 6", XivChatType.CrossLinkShell6),
        ("CWLS 7", XivChatType.CrossLinkShell7),
        ("CWLS 8", XivChatType.CrossLinkShell8),
    ];

    public string Name => "Formatting";
    public FontAwesomeIcon Icon => FontAwesomeIcon.Font;

    private readonly Configuration mutable;
    private readonly UiColorPicker colorPicker = new();

    public FormattingTab(Configuration mutable)
    {
        this.mutable = mutable;
    }

    public void Draw()
    {
        SettingsUi.SectionHeader("Color selection for text types",
            "Each enabled type recolors matching chat text: Color is the text color, Glow its outline. "
            + "Right-click a swatch to clear it.");
        DrawSegmentColors();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader("Channels with RP formatting");
        DrawChannels();
    }

    private void DrawSegmentColors()
    {
        using var table = ImRaii.Table("##segmentColors", 4, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 90f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Color");
        ImGui.TableSetupColumn("Glow");
        ImGui.TableSetupColumn("Delimiters", ImGuiTableColumnFlags.WidthStretch);

        DrawSegmentRow("Say", mutable.SayStyle, "\"…\"  „…“  «…»");
        DrawSegmentRow("Emote", mutable.EmoteStyle, "*…*  <…>");
        DrawSegmentRow("OOC", mutable.OocStyle, "((…))");
        DrawSegmentRow("Mention", mutable.MentionStyle, "trigger words");
    }

    private void DrawSegmentRow(string label, SegmentStyle style, string delimiters)
    {
        using var id = ImRaii.PushId(label);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var enabled = style.Enabled;
        if (ImGui.Checkbox(label, ref enabled))
            style.Enabled = enabled;

        using var disabled = ImRaii.Disabled(!style.Enabled);

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

    private void DrawChannels()
    {
        DrawChannelGrid("##channels-main", MainChannels);

        if (ImGui.CollapsingHeader("Linkshells"))
            DrawChannelGrid("##channels-ls", LinkshellChannels);

        if (ImGui.CollapsingHeader("Cross-world Linkshells"))
            DrawChannelGrid("##channels-cwls", CrossworldLinkshellChannels);

        ImGuiHelpers.ScaledDummy(2f);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Undo, "Reset to RP defaults"))
            mutable.HighlightChannels = [.. Configuration.DefaultHighlightChannels];
    }

    private void DrawChannelGrid(string id, (string Label, XivChatType Type)[] choices)
    {
        using var table = ImRaii.Table(id, 3);
        if (!table)
            return;

        foreach (var (label, type) in choices)
        {
            ImGui.TableNextColumn();
            var active = mutable.HighlightChannels.Contains(type);
            if (!ImGui.Checkbox(label, ref active))
                continue;

            if (active)
                mutable.HighlightChannels.Add(type);
            else
                mutable.HighlightChannels.Remove(type);
        }
    }
}
