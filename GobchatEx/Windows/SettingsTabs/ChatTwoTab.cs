using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// "Tab settings" page (Milestone 3.5): per-Chat 2-tab switches for which styling (backgrounds /
/// fading / hiding) may apply there. The tab list comes live from
/// <see cref="ChatTwoStyleProvider.KnownTabs"/>; the policies themselves live in the
/// configuration like every other setting and are pushed to Chat 2 when the window commits a
/// change. Checked = allowed (the default); unchecked stores the corresponding suppress-flag.
/// Connection status lives on the General page's Optional plugins row (plus, in debug builds,
/// the footer's connect/disconnect control).
/// </summary>
internal sealed class ChatTwoTab : ISettingsTab
{
    public string Name => Loc.Get("ChatTwo_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.CommentDots;

    private readonly Configuration config;
    private readonly ChatTwoStyleProvider chatTwoStyles;

    public ChatTwoTab(Configuration config, ChatTwoStyleProvider chatTwoStyles)
    {
        this.config = config;
        this.chatTwoStyles = chatTwoStyles;
    }

    public void Draw()
    {
        if (!chatTwoStyles.IsConnected)
            ImGui.TextDisabled(Loc.Get("ChatTwo_NotConnected_Hint"));

        SettingsUi.SectionHeader(Loc.Get("ChatTwo_Tabs_Header"), Loc.Get("ChatTwo_Tabs_Header_Tooltip"));

        using var disabled = ImRaii.Disabled(!chatTwoStyles.IsConnected);
        DrawTabPolicies();
    }

    private void DrawTabPolicies()
    {
        var tabs = chatTwoStyles.KnownTabs;
        if (tabs.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("ChatTwo_Tabs_Empty"));
            return;
        }

        using var table = ImRaii.Table("##chattwo-tab-policies", 4, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(Loc.Get("ChatTwo_Policy_Column_Tab"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.Get("ChatTwo_Policy_Column_Backgrounds"));
        ImGui.TableSetupColumn(Loc.Get("ChatTwo_Policy_Column_Fading"));
        ImGui.TableSetupColumn(Loc.Get("ChatTwo_Policy_Column_Hiding"));
        ImGui.TableHeadersRow();

        foreach (var (id, name) in tabs)
        {
            using var pushId = ImRaii.PushId(id.ToString());

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(name);

            var flags = config.ChatTwoTabPolicies.GetValueOrDefault(id);
            var changed = false;

            ImGui.TableNextColumn();
            changed |= DrawAllowedCheckbox("##bg", ChatTwoStyleProvider.SuppressBackground, ref flags);

            ImGui.TableNextColumn();
            changed |= DrawAllowedCheckbox("##fade", ChatTwoStyleProvider.SuppressFade, ref flags);

            ImGui.TableNextColumn();
            changed |= DrawAllowedCheckbox("##hide", ChatTwoStyleProvider.SuppressHide, ref flags);

            if (!changed)
                continue;

            // No entry means "everything allowed" — keep the config free of default entries.
            if (flags == 0)
                config.ChatTwoTabPolicies.Remove(id);
            else
                config.ChatTwoTabPolicies[id] = flags;
        }
    }

    /// <summary>Checkbox showing "allowed" while the config stores the inverse suppress-flag.</summary>
    private static bool DrawAllowedCheckbox(string id, int suppressFlag, ref int flags)
    {
        var allowed = (flags & suppressFlag) == 0;
        if (!ImGui.Checkbox(id, ref allowed))
            return false;

        flags = allowed ? flags & ~suppressFlag : flags | suppressFlag;
        return true;
    }
}
