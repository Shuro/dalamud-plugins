#if DEBUG
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Dev page, split into an ImGui tab bar with one tab per test area: the Chat 2 message styling
/// IPC (exercised through <see cref="ChatTwoStyleIpcTester"/>) and the native range dimming
/// (<see cref="DebugRangePane"/>). Unlike the other pages everything here works live — against
/// Chat 2 or the saved configuration — nothing is staged or persisted, and Save/Apply/Cancel
/// don't affect it. Body strings stay unlocalized on purpose: developer tooling, not user-facing
/// UI.
/// </summary>
internal sealed class DebugTab : ISettingsTab
{
    private static readonly Vector4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);

    private readonly ChatTwoStyleIpcTester tester;
    private readonly DebugRangePane rangePane;

    public string Name => Loc.Get("Debug_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Bug;

    public DebugTab(Plugin plugin)
    {
        tester = plugin.ChatTwoStyleTester;
        rangePane = new DebugRangePane(plugin);
    }

    public void Draw()
    {
        using var tabBar = ImRaii.TabBar("##debug-tabs");
        if (!tabBar)
            return;

        using (var ipcTab = ImRaii.TabItem("Chat 2 IPC"))
        {
            if (ipcTab)
                DrawChatTwoIpc();
        }

        using (var rangeTab = ImRaii.TabItem("Range dimming"))
        {
            if (rangeTab)
                rangePane.Draw();
        }
    }

    private void DrawChatTwoIpc()
    {
        DrawConnection();
        ImGui.Separator();
        DrawRule();
        ImGui.Separator();
        DrawTabPolicies();
        ImGui.Separator();
        DrawInvocationLog();
    }

    private void DrawConnection()
    {
        ImGui.TextDisabled("Styling IPC connection");

        if (ImGui.Button("Query ChatTwo.StyleVersion"))
            tester.QueryVersion();
        ImGui.SameLine();
        ImGui.TextUnformatted(tester.LastVersion is { } version ? $"version {version}" : "version unknown");

        if (tester.Registered)
        {
            if (ImGui.Button("Unregister provider"))
                tester.Unregister();
            ImGui.SameLine();
            ImGui.TextUnformatted($"registered as {ChatTwoStyleIpcTester.ProviderGateName}");
        }
        else
        {
            if (ImGui.Button("Register provider"))
                tester.Register();
            ImGui.SameLine();
            ImGui.TextUnformatted("not registered");
        }

        ImGui.Checkbox("Re-register when Chat 2 reloads", ref tester.AutoReregister);
        ImGui.SameLine();
        ImGui.TextDisabled($"ChatTwo.Available events: {tester.AvailableCount}");

        if (tester.LastError is { } error)
            ImGui.TextColored(ErrorColor, $"Last error: {error}");
    }

    private void DrawRule()
    {
        ImGui.TextDisabled("Test rule — styles are stamped at ingestion, so changes apply to new messages only");

        ImGui.Checkbox("Rule enabled", ref tester.RuleEnabled);

        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        ImGui.InputText("Sender contains (empty = all)", ref tester.RuleNameFilter, 64);

        ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt("Chat type (-1 = all)", ref tester.RuleChatTypeFilter);
        if (tester.RuleChatTypeFilter >= 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"= {(XivChatType)tester.RuleChatTypeFilter}");
        }

        ImGui.Checkbox("Apply background", ref tester.RuleApplyBackground);
        ImGui.SameLine();
        ImGui.ColorEdit4("##debug-style-bg", ref tester.RuleBackground, ImGuiColorEditFlags.NoInputs);

        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("Alpha (0 = hidden, 1 = normal)", ref tester.RuleAlpha, 0f, 1f);
    }

    private void DrawTabPolicies()
    {
        ImGui.TextDisabled("Tabs & per-tab policies");

        if (ImGui.Button("Query ChatTwo.GetTabs"))
            tester.RefreshTabs();
        ImGui.SameLine();
        var last = tester.LastTabsChangedAt is { } at ? $", last {at:HH:mm:ss}" : "";
        ImGui.TextDisabled($"ChatTwo.TabsChanged events: {tester.TabsChangedCount}{last}");

        if (tester.KnownTabs.Count == 0)
        {
            ImGui.TextUnformatted("No tabs known — query or wait for a TabsChanged push.");
            return;
        }

        foreach (var (id, name) in tester.KnownTabs)
        {
            using var pushId = ImRaii.PushId(id.ToString());

            var flags = tester.PolicyDraft.GetValueOrDefault(id);
            var noBackground = (flags & ChatTwoStyleIpcTester.SuppressBackground) != 0;
            var noFade = (flags & ChatTwoStyleIpcTester.SuppressFade) != 0;
            var noHide = (flags & ChatTwoStyleIpcTester.SuppressHide) != 0;

            var changed = ImGui.Checkbox("no bg", ref noBackground);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("no fade", ref noFade);
            ImGui.SameLine();
            changed |= ImGui.Checkbox("no hide", ref noHide);
            ImGui.SameLine();
            ImGui.TextUnformatted(name);
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted(id.ToString());
            }

            if (!changed)
                continue;

            var newFlags = (noBackground ? ChatTwoStyleIpcTester.SuppressBackground : 0)
                           | (noFade ? ChatTwoStyleIpcTester.SuppressFade : 0)
                           | (noHide ? ChatTwoStyleIpcTester.SuppressHide : 0);
            if (newFlags == 0)
                tester.PolicyDraft.Remove(id);
            else
                tester.PolicyDraft[id] = newFlags;
        }

        if (ImGui.Button("Send policies (ChatTwo.SetTabStylePolicies)"))
            tester.SendPolicies();
        ImGui.SameLine();
        if (ImGui.Button("Clear & send"))
        {
            tester.PolicyDraft.Clear();
            tester.SendPolicies();
        }
    }

    private void DrawInvocationLog()
    {
        var snapshot = tester.SnapshotInvocations();

        ImGui.TextDisabled($"Provider invocations: {tester.InvocationCount} total, newest first");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
            tester.ClearInvocations();

        using var table = ImRaii.Table("##debug-invocations", 7,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new Vector2(-1, 220f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Sender");
        ImGui.TableSetupColumn("World");
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Raw sender");
        ImGui.TableSetupColumn("Content");
        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            var invocation = snapshot[i];

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(invocation.Time.ToString("HH:mm:ss"));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(invocation.SenderName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(invocation.SenderWorld);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(invocation.ChatType.ToString());
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted($"{(XivChatType)invocation.ChatType} (content ID {invocation.ContentId:X})");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(invocation.SenderRaw);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Truncate(invocation.ContentText, 60));
            if (invocation.ContentText.Length > 60 && ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted(invocation.ContentText);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"bg {invocation.ReturnedBackground:X8}, a {invocation.ReturnedAlpha:0.00}");
        }
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
#endif
