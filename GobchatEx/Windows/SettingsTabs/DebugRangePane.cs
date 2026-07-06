using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Core;
using Lumina.Excel.Sheets;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// "Range dimming" pane of the Debug page: exercises the native range filter against the live
/// configuration (Debug page convention — config-read-only, body strings unlocalized). Three tools:
/// a distance simulator over the pure <see cref="RangeFade"/> math, a live object-table view of
/// what the filter would assign each nearby player right now, and test-message injection printing
/// native chat lines in each fade-step color — <see cref="ChatListener.FadeStepColors"/> is a
/// provisional pick, and legibility depends on the user's chat theme.
/// </summary>
internal sealed class DebugRangePane
{
    private readonly Plugin plugin;

    // ImGui previews of the fade-step UIColor rows, decoded from the sheet's Dark field exactly
    // like UiColorPicker. Resolved once; the sheet is static data.
    private readonly Vector4[] stepPreviewColors;

    private float simulatedDistance = 20f;

    public DebugRangePane(Plugin plugin)
    {
        this.plugin = plugin;

        var sheet = Plugin.DataManager.GetExcelSheet<UIColor>();
        stepPreviewColors = new Vector4[ChatListener.FadeStepColors.Length];
        for (var i = 0; i < stepPreviewColors.Length; i++)
        {
            stepPreviewColors[i] = sheet.TryGetRow(ChatListener.FadeStepColors[i], out var row)
                ? DecodeRgba(row.Dark)
                : new Vector4(0.5f, 0.5f, 0.5f, 1f);
        }
    }

    public void Draw()
    {
        DrawConfigSummary();
        ImGui.Separator();
        DrawSimulator();
        ImGui.Separator();
        DrawNearbyPlayers();
        ImGui.Separator();
        DrawInjection();
    }

    private void DrawConfigSummary()
    {
        var config = plugin.Configuration.RangeFilter;
        ImGui.TextDisabled("Live settings — edits on the Range page show here as soon as they commit");
        ImGui.TextUnformatted(
            $"Range filter {(config.RangeFilterEnabled ? "enabled" : "disabled (tools below still evaluate the configured distances)")}"
            + $", fade-out {config.RangeFilterFadeOut:0} yalms, cut-off {config.RangeFilterCutOff:0} yalms");
    }

    private void DrawSimulator()
    {
        ImGui.TextDisabled("Distance simulator");

        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("Distance##debug-range-sim", ref simulatedDistance, 0f, 100f, "%.1f yalms");

        var config = plugin.Configuration.RangeFilter;
        var visibility = RangeFade.CalculateVisibility(
            simulatedDistance, config.RangeFilterFadeOut, config.RangeFilterCutOff);
        DrawOutcome(visibility);
    }

    private void DrawNearbyPlayers()
    {
        ImGui.TextDisabled("Nearby players — what the filter would assign right now");

        var local = Plugin.ObjectTable.LocalPlayer;
        if (local == null)
        {
            ImGui.TextUnformatted("Not logged in.");
            return;
        }

        var config = plugin.Configuration.RangeFilter;

        using var table = ImRaii.Table("##debug-range-players", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new Vector2(-1, 180f * ImGuiHelpers.GlobalScale));
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("World");
        ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Result");
        ImGui.TableHeadersRow();

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IPlayerCharacter pc)
                continue;

            var distance = Vector3.Distance(local.Position, pc.Position);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(pc.GameObjectId == local.GameObjectId
                ? $"{pc.Name.TextValue} (you)"
                : pc.Name.TextValue);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? "?");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{distance:0.0}");

            ImGui.TableNextColumn();
            var visibility = RangeFade.CalculateVisibility(
                distance, config.RangeFilterFadeOut, config.RangeFilterCutOff);
            DrawOutcome(visibility);
        }
    }

    private void DrawInjection()
    {
        ImGui.TextDisabled("Test messages — printed to the native log to judge step legibility");

        if (ImGui.Button("Uncolored reference"))
            Plugin.ChatGui.Print("GobchatEx range test — uncolored reference");

        for (var step = 0; step < ChatListener.FadeStepColors.Length; step++)
        {
            ImGui.SameLine();
            if (!ImGui.Button($"Step {step}"))
                continue;

            var row = ChatListener.FadeStepColors[step];
            var builder = new SeStringBuilder();
            builder.AddUiForeground(row);
            builder.AddText($"GobchatEx range test — fade step {step} (UIColor row {row})");
            builder.AddUiForegroundOff();
            Plugin.ChatGui.Print(builder.Build());

            // Second line in the dimmed emote color, to eyeball UiColorDimmer's darker-row
            // mapping — colored spans keep their hue when faded.
            var emoteRow = plugin.Configuration.Formatting.EmoteStyle.Foreground;
            if (emoteRow == 0)
                continue;

            var dimmedRow = UiColorDimmer.DimRow(emoteRow, step);
            var colored = new SeStringBuilder();
            colored.AddUiForeground(dimmedRow);
            colored.AddText($"GobchatEx range test — emote color at step {step} (row {emoteRow} → {dimmedRow})");
            colored.AddUiForegroundOff();
            Plugin.ChatGui.Print(colored.Build());
        }
    }

    /// <summary>One outcome line/cell: full, hidden, or the fade step in its actual color.</summary>
    private void DrawOutcome(int visibility)
    {
        if (visibility == RangeFade.MaxVisibility)
        {
            ImGui.TextUnformatted("100% — fully visible");
            return;
        }

        if (visibility == 0)
        {
            ImGui.TextColored(stepPreviewColors[^1],
                "0% — beyond cut-off: darkest step (Vanilla never removes; Chat 2 may hide render-only)");
            return;
        }

        var step = RangeFade.FadeStep(visibility, ChatListener.FadeStepColors.Length);
        ImGui.TextColored(stepPreviewColors[step],
            $"{visibility}% — fade step {step} (UIColor row {ChatListener.FadeStepColors[step]})");
    }

    private static Vector4 DecodeRgba(uint rgba) => new(
        ((rgba >> 24) & 255) / 255f,
        ((rgba >> 16) & 255) / 255f,
        ((rgba >> 8) & 255) / 255f,
        (rgba & 255) / 255f);
}
