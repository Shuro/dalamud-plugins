#if DEBUG
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// "Friend Groups" pane of the Debug page: shows the live <c>FriendGroupLookup</c> snapshot
/// (Debug page convention — config-read-only, body strings unlocalized). Includes a manual refresh button
/// because, unlike the removed production one, this is the only way to force a fresh snapshot mid
/// dev-session without relogging (dev auto-reload never fires Dalamud's Login event).
/// </summary>
internal sealed class DebugGroupsPane
{
    private readonly Plugin plugin;

    public DebugGroupsPane(Plugin plugin) => this.plugin = plugin;

    public void Draw()
    {
        DrawRefresh();
        ImGui.Separator();
        DrawEntries();
    }

    private void DrawRefresh()
    {
        ImGui.TextDisabled("Live snapshot of InfoProxyFriendList — refreshes on login/plugin load; " +
                            "force it here to see in-game friend-group edits without relogging");

        if (ImGui.Button("Refresh now"))
            plugin.FriendGroups.Refresh();
    }

    private void DrawEntries()
    {
        var entries = plugin.FriendGroups.Entries;
        ImGui.TextUnformatted($"{entries.Count} entries");

        using var table = ImRaii.Table("##debug-friend-groups", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new Vector2(-1, 220f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("World");
        ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var ((name, world), ffGroup) in entries)
        {
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(name);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(world);

            ImGui.TableNextColumn();
            // ffGroup is the 0..6 index (Star..Club); the enum is 1..7 (see FriendGroupLookup.Refresh).
            ImGui.TextUnformatted(((InfoProxyCommonList.DisplayGroup)(ffGroup + 1)).ToString());
        }
    }
}
#endif
