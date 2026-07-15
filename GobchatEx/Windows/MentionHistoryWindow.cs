using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using GobchatEx.Chat;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Windows;

/// <summary>
/// Recent mentions (Milestone 7): a newest-first table over <see cref="MentionHistory"/> —
/// "was I mentioned while AFK or in another window". Long messages show in full in a hover
/// tooltip (the native chat log can't be scrolled to a line, so the window carries the message
/// itself); right-clicking a sender offers the same add/remove-group actions as the chat
/// context menu. Toggled from the Quickbar; in-memory only, cleared with the plugin.
/// </summary>
public class MentionHistoryWindow : Window
{
    private readonly Plugin plugin;

    public MentionHistoryWindow(Plugin plugin)
        : base($"{Loc.Get("MentionHistory_Title")}###GobchatExMentionHistory")
    {
        this.plugin = plugin;
        Size = new Vector2(520, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 160),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    // The title carries the localized name; refresh it when the language changes (the ### id
    // keeps ImGui treating it as the same window).
    public override void PreDraw()
        => WindowName = $"{Loc.Get("MentionHistory_Title")}###GobchatExMentionHistory";

    public override void Draw()
    {
        var history = plugin.MentionHistory;

        if (ImGui.Button(Loc.Get("MentionHistory_Clear")))
            history.Clear();

        if (history.Entries.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("MentionHistory_Empty"));
            return;
        }

        // "V2" because ImGui persists resizable-table column widths by ID: the 4-column
        // layout's saved settings applied the old stretchy Message width to whatever column
        // sat at that index — the new fixed Mentions column came out absurdly wide. A fresh
        // ID sheds the stale settings (the plugin is unpublished, nothing to migrate).
        using var table = ImRaii.Table("##mentionHistoryV2", 5,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupColumn(Loc.Get("MentionHistory_Column_Time"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("MentionHistory_Column_Channel"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("MentionHistory_Column_Sender"), ImGuiTableColumnFlags.WidthFixed,
            140f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn(Loc.Get("MentionHistory_Column_Mentions"), ImGuiTableColumnFlags.WidthFixed,
            100f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn(Loc.Get("MentionHistory_Column_Message"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupScrollFreeze(0, 1); // keep the header row visible while scrolling
        ImGui.TableHeadersRow();

        var mentionColor = RgbaColor.ToVector4(plugin.Configuration.Formatting.MentionStyle.Foreground);

        var entries = history.Entries;
        for (var i = entries.Count - 1; i >= 0; --i)
        {
            var entry = entries[i];
            // Sequence, not the loop index: the index shifts when the capacity eviction drops
            // the oldest entry, which would re-bind an open sender context popup (its ImGui
            // state is keyed by this id across frames) to a different player mid-click.
            using var id = ImRaii.PushId(entry.Sequence);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Timestamp.ToString("HH:mm"));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ChatLogChannelNames.Get(entry.Channel));

            ImGui.TableNextColumn();
            // Selectable (not plain text) so the row has an item id for the context popup.
            ImGui.Selectable(GroupMembershipActions.FormatPlayer(entry.SenderName, entry.SenderWorld));
            SettingsUi.Tooltip(Loc.Get("MentionHistory_Sender_Tooltip"));
            if (ImGui.BeginPopupContextItem("##senderContext"))
            {
                DrawGroupContext(entry);
                ImGui.EndPopup();
            }

            // What actually triggered (distinct matched texts), in the mention color. Empty
            // when the match couldn't be reproduced on the original text (see RecordMention).
            ImGui.TableNextColumn();
            ImGui.TextColored(mentionColor, entry.Matches);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Message);
            if (ImGui.IsItemHovered())
            {
                using var tooltip = ImRaii.Tooltip();
                if (entry.MentionSpans.Count > 0)
                {
                    SettingsUi.HighlightedTextWrapped(entry.Message, entry.MentionSpans,
                        mentionColor, 400f * ImGuiHelpers.GlobalScale);
                }
                else
                {
                    ImGui.PushTextWrapPos(400f * ImGuiHelpers.GlobalScale);
                    ImGui.TextWrapped(entry.Message);
                    ImGui.PopTextWrapPos();
                }
            }
        }
    }

    /// <summary>Same actions and wording as the chat context menu's Groups submenu
    /// (<see cref="Plugin.OnMenuOpened"/>), in ImGui popup form.</summary>
    private void DrawGroupContext(MentionHistoryEntry entry)
    {
        var groups = plugin.Configuration.Groups.Groups;
        if (groups.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Groups_ContextMenu_None"));
            return;
        }

        var actions = new GroupMembershipActions(plugin, entry.SenderName, entry.SenderWorld);
        foreach (var group in groups)
        {
            var inGroup = actions.IsInGroup(group);
            var label = string.Format(
                Loc.Get(inGroup ? "Groups_ContextMenu_RemoveFrom" : "Groups_ContextMenu_AddTo"), group.Name);
            if (!ImGui.MenuItem(label))
                continue;

            if (inGroup)
                actions.RemoveFromGroup(group);
            else
                actions.AddToGroup(group);
        }
    }
}
