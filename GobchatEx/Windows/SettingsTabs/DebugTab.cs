#if DEBUG
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using GobchatEx.Chat;
using GobchatEx.Localization;
using LSeStringBuilder = Lumina.Text.SeStringBuilder;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Dev page, split into an ImGui tab bar with one tab per test area: the Chat 2 message styling
/// IPC (exercised through <see cref="ChatTwoStyleIpcTester"/>), the native range dimming
/// (<see cref="DebugRangePane"/>), live friend-group state (<see cref="DebugGroupsPane"/>), and
/// glow/color macro probes printed to the native log. Unlike the other pages nothing here edits
/// the configuration — it works live against Chat 2 or reads the saved config, so the window's
/// instant-apply commit never fires for it. Body strings stay unlocalized on purpose: developer
/// tooling, not user-facing UI.
/// </summary>
internal sealed class DebugTab : ISettingsTab
{
    private static readonly Vector4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);

    private readonly ChatTwoStyleIpcTester tester;
    private readonly DebugRangePane rangePane;
    private readonly DebugGroupsPane groupsPane;

    private string customColorText = "GobchatEx custom color test";
    private Vector4 customTextColor = new(1f, 1f, 1f, 1f);
    private Vector4 customGlowColor = new(0.125f, 1f, 0.125f, 1f);

    public string Name => Loc.Get("Debug_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Bug;

    public DebugTab(Plugin plugin)
    {
        tester = plugin.ChatTwoStyleTester;
        rangePane = new DebugRangePane(plugin);
        groupsPane = new DebugGroupsPane(plugin);
    }

    public void Draw()
    {
        using var tabBar = ImRaii.TabBar("##debug-tabs");
        if (!tabBar)
            return;

        using (var generalTab = ImRaii.TabItem("General"))
        {
            if (generalTab)
                DrawGeneral();
        }

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

        using (var groupsTab = ImRaii.TabItem("Friend Groups"))
        {
            if (groupsTab)
                groupsPane.Draw();
        }

        using (var glowTab = ImRaii.TabItem("Glow test"))
        {
            if (glowTab)
                DrawGlowInjection();
        }
    }

    private void DrawGeneral()
    {
        ImGui.TextDisabled("General — misc one-off probes");

        if (ImGui.Button("Send rainbow A-Z (Echo)"))
        {
            var b = new LSeStringBuilder();
            for (var i = 0; i < 26; i++)
            {
                var hue = i / 26f;
                var rgb = ColorHelpers.HsvToRgb(new ColorHelpers.HsvaColor(hue, 1f, 1f, 1f));
                b.PushColorBgra(ChatColor.PackAarrggbb(rgb));
                b.Append(((char)('A' + i)).ToString());
                b.PopColor();
            }

            Plugin.ChatGui.Print(new XivChatEntry { Message = b.ToReadOnlySeString().ToDalamudString(), Type = XivChatType.Echo });
        }
    }

    // Exercises every EdgeColor/EdgeColorType parse path of the Chat 2 glow fix in one
    // message. The native log renders the identical bytes with the game's renderer, so
    // vanilla chat is the ground truth to compare Chat 2 against.
    private void DrawGlowInjection()
    {
        ImGui.TextDisabled("Chat 2 glow test — compare the line against the native log");

        if (ImGui.Button("Send EdgeColor test message"))
        {
            // Raw EdgeColor macro chunks (0x14), full envelope incl. 0x02/0x03 bytes.
            // 3-byte packed int, no alpha byte (numeric 0x0020FF20) — common in-the-wild form.
            var rawGreen = new RawPayload([0x02, 0x14, 0x05, 0xF6, 0x20, 0xFF, 0x20, 0x03]);
            // 4-byte packed int with explicit alpha 0x80 (numeric 0x80FF40FF) — the reviewer's
            // case; the game ignores edge-color alpha, so this must glow at full strength.
            var rawPink = new RawPayload([0x02, 0x14, 0x06, 0xFE, 0x80, 0xFF, 0x40, 0xFF, 0x03]);
            // edgecolor(gnum(4)) — global parameter push; resolved color depends on client
            // state, but it must never pop the glow below it.
            var rawGnum = new RawPayload([0x02, 0x14, 0x03, 0xE9, 0x05, 0x03]);
            // edgecolor(stackcolor) — pops one push off the edge-color stack.
            var pop = new RawPayload([0x02, 0x14, 0x02, 0xEC, 0x03]);

            var b = new SeStringBuilder();
            b.AddText("plain ");
            b.AddUiGlow(500);        // sheet push (EdgeColorType / UIGlowPayload)
            b.AddText("sheet ");
            b.Add(rawGreen);         // raw push on top of a sheet glow — old parser popped here
            b.AddText("raw-green ");
            b.Add(rawPink);
            b.AddText("raw-pink ");
            b.Add(rawGnum);
            b.AddText("gnum ");
            b.Add(pop);
            b.AddText("pink-again ");
            b.Add(pop);
            b.AddText("green-again ");
            b.Add(pop);
            b.AddText("sheet-again ");
            b.AddUiGlowOff();        // sheet pop (EdgeColorType 0)
            b.AddText("plain-again");
            Plugin.ChatGui.Print(b.Build());
        }

        if (ImGui.Button("Send literal small-int test message"))
        {
            // Literal <edgecolor(0)> — small-int encoding, single byte value+1 = 0x01.
            // Settles whether the game pushes a literal 0 (no glow, like the gnum-0 case)
            // or duplicates the stack top (what Dalamud's reimplementation claims).
            var litZero = new RawPayload([0x02, 0x14, 0x02, 0x01, 0x03]);
            // Literal <edgecolor(200)> = numeric 0x000000C8, single byte 0xC9 — a barely
            // visible dark blue; its job is structural (does it push?), not looks.
            var lit200 = new RawPayload([0x02, 0x14, 0x02, 0xC9, 0x03]);
            var pop = new RawPayload([0x02, 0x14, 0x02, 0xEC, 0x03]);

            var b = new SeStringBuilder();
            b.AddText("plain ");
            b.AddUiGlow(500);            // sheet push as the outer reference glow
            b.AddText("sheet ");
            b.Add(litZero);
            b.AddText("lit-zero ");
            b.Add(lit200);
            b.AddText("lit-200 ");
            b.Add(pop);
            b.AddText("after-pop1 ");
            b.Add(pop);
            b.AddText("after-pop2 ");
            b.AddUiGlowOff();
            b.AddText("plain-again");
            Plugin.ChatGui.Print(b.Build());
        }

        if (ImGui.Button("Send sheet glow test message"))
        {
            // edgecolor(stackcolor) — the docs-recommended way to pop, even for sheet pushes
            var pop = new RawPayload([0x02, 0x14, 0x02, 0xEC, 0x03]);

            var b = new SeStringBuilder();
            b.AddText("plain ");
            b.AddUiGlow(500);        // sheet push #1
            b.AddText("sheet1 ");
            b.AddUiGlow(517);        // sheet push #2, nested — swap the row if the hue is too close to 500
            b.AddText("sheet2 ");
            b.AddUiGlowOff();        // EdgeColorType(0) → pops the inner sheet glow
            b.AddText("sheet1-again ");
            b.Add(pop);              // raw stackcolor popping a SHEET push (cross-family)
            b.AddText("plain-again");
            Plugin.ChatGui.Print(b.Build());
        }

        if (ImGui.Button("Send invalid sheet row"))
        {
            var b = new SeStringBuilder();
            b.AddText("before ");
            b.AddUiGlow(9999);       // row absent from the UIColor sheet
            b.AddText("invalid-row ");
            b.AddUiGlowOff();
            b.AddText("after");
            Plugin.ChatGui.Print(b.Build());
        }

        ImGui.Separator();
        DrawCustomColorInjection();
    }

    // Free-form probe: raw Color (0x13) / EdgeColor (0x14) pushes with an arbitrary packed
    // 0xAARRGGBB value from the pickers — no UIColor sheet involved. The game ignores the
    // alpha byte for edge colors, so the alpha slider on the glow picker tests exactly that.
    private void DrawCustomColorInjection()
    {
        ImGui.TextDisabled("Custom colors — raw Color/EdgeColor macros with arbitrary packed RGBA");

        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        ImGui.InputText("Message text", ref customColorText, 256);

        var pickerWidth = 210f * ImGuiHelpers.GlobalScale;
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted("Text color");
            ImGui.SetNextItemWidth(pickerWidth);
            ImGui.ColorPicker4("##debug-custom-text-color", ref customTextColor, ImGuiColorEditFlags.AlphaBar);
            ImGui.TextDisabled($"packed 0x{ChatColor.PackAarrggbb(customTextColor):X8}");
        }
        ImGui.SameLine(0f, 24f * ImGuiHelpers.GlobalScale);
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted("Glow color");
            ImGui.SetNextItemWidth(pickerWidth);
            ImGui.ColorPicker4("##debug-custom-glow-color", ref customGlowColor, ImGuiColorEditFlags.AlphaBar);
            ImGui.TextDisabled($"packed 0x{ChatColor.PackAarrggbb(customGlowColor):X8}");
        }

        if (ImGui.Button("Send custom color message"))
        {
            var text = string.IsNullOrWhiteSpace(customColorText) ? "GobchatEx custom color test" : customColorText;
            var b = new LSeStringBuilder();
            b.PushColorBgra(ChatColor.PackAarrggbb(customTextColor));
            b.PushEdgeColorBgra(ChatColor.PackAarrggbb(customGlowColor));
            b.Append(text);
            b.PopEdgeColor();
            b.PopColor();
            Plugin.ChatGui.Print(b.ToReadOnlySeString().ToDalamudString());
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
