#if DEBUG
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Core;
using Lumina.Excel.Sheets;
using Lumina.Text;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// "Range dimming" pane of the Debug page: exercises the native range filter against the live
/// configuration (Debug page convention — config-read-only, body strings unlocalized). Three tools:
/// a distance simulator over the pure <see cref="RangeFade"/> math, a live object-table view of
/// what the filter would assign each nearby player right now, and test-message injection printing
/// native chat lines in each fade-step color — RP-highlight colors are provisional picks and
/// legibility depends on the user's chat theme, while the per-channel native colors come from
/// <see cref="ChatListener.ResolveChannelColorWithSource"/> (Chat 2's own customized color when on
/// file, else the player's vanilla Log Text Color settings) and are labeled with whichever source
/// won.
/// </summary>
internal sealed class DebugRangePane
{
    private readonly Plugin plugin;

    // ImGui previews of the fade-step UIColor rows, decoded from the sheet's Dark field.
    // Resolved once; the sheet is static data.
    private readonly Vector4[] stepPreviewColors;

    private float simulatedDistance = 20f;
    private bool showFromToColors;

    public DebugRangePane(Plugin plugin)
    {
        this.plugin = plugin;

        var sheet = Plugin.DataManager.GetExcelSheet<UIColor>();
        stepPreviewColors = new Vector4[ChatListener.FadeStepColors.Length];
        for (var i = 0; i < stepPreviewColors.Length; i++)
        {
            stepPreviewColors[i] = sheet.TryGetRow(ChatListener.FadeStepColors[i], out var row)
                ? RgbaColor.ToVector4(row.Dark)
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
        ImGui.Checkbox("Also print From -> To hex reference lines", ref showFromToColors);

        for (var step = 0; step < ChatListener.FadeStepColors.Length; step++)
        {
            if (step > 0)
                ImGui.SameLine();

            if (ImGui.Button($"Step {step}"))
                PrintRangeTestMessage(step);
        }
    }

    /// <summary>
    /// One "GEX range test" line mixing all four highlight categories at <paramref name="step"/>,
    /// each with Foreground and Glow dimmed independently via <see cref="UiColorDimmer.DimRgba"/> —
    /// eyeballs the exact colors and glow a real faded message would use. A category with no
    /// configured Foreground renders its span as plain default-colored text instead of being
    /// omitted, so the line's wording is identical across configurations. A second line then shows
    /// plain, unformatted text per range-filterable channel, each in that channel's own configured
    /// color (<see cref="PrintChannelNativeFade"/>) — the fix for GEX previously collapsing every
    /// channel's unmarked text into one shared grey. When <see cref="showFromToColors"/> is set,
    /// also prints one "Category: 0xFROM -> 0xTO" line per highlight category underneath, with FROM
    /// in the category's base Foreground color and TO in that color dimmed to <paramref name="step"/>.
    /// </summary>
    private void PrintRangeTestMessage(int step)
    {
        var formatting = plugin.Configuration.Formatting;
        var emote = (formatting.EmoteStyle.Foreground, formatting.EmoteStyle.Glow);
        var say = (formatting.SayStyle.Foreground, formatting.SayStyle.Glow);
        var ooc = (formatting.OocStyle.Foreground, formatting.OocStyle.Glow);
        var mention = (formatting.MentionStyle.Foreground, formatting.MentionStyle.Glow);

        var builder = new SeStringBuilder();
        AppendFadeSegment(builder, emote, step, "GEX range test - ");
        AppendFadeSegment(builder, say, step, "\"say\"");
        AppendFadeSegment(builder, emote, step, " ");
        AppendFadeSegment(builder, ooc, step, "((ooc))");
        AppendFadeSegment(builder, emote, step, " ");
        AppendFadeSegment(builder, mention, step, "Mention");
        AppendFadeSegment(builder, emote, step, $" at step {step}");
        Plugin.ChatGui.Print(builder.ToReadOnlySeString().ToDalamudString());

        PrintChannelNativeFade(step);

        if (!showFromToColors)
            return;

        PrintFromTo("Emote", emote, step);
        PrintFromTo("Say", say, step);
        PrintFromTo("Ooc", ooc, step);
        PrintFromTo("Mention", mention, step);
    }

    // A category's Foreground/Glow of 0 means disabled/unset — skip pushing that macro instead of
    // pushing a meaningless (would-render-transparent) color. Mirrors PayloadRewriter.AppendColored's
    // independent Foreground/Glow push-then-pop nesting (Color outermost, EdgeColor innermost).
    private static void AppendFadeSegment(SeStringBuilder builder, (uint Foreground, uint Glow) style, int step, string text)
    {
        var foreground = style.Foreground == 0 ? 0 : UiColorDimmer.DimRgba(style.Foreground, step);
        var glow = style.Glow == 0 ? 0 : UiColorDimmer.DimRgba(style.Glow, step);

        if (foreground != 0)
            builder.PushColorBgra(ChatColor.ToOpaqueAarrggbb(foreground));
        if (glow != 0)
            builder.PushEdgeColorBgra(ChatColor.ToOpaqueAarrggbb(glow));
        builder.Append(text);
        if (glow != 0)
            builder.PopEdgeColor();
        if (foreground != 0)
            builder.PopColor();
    }

    /// <summary>
    /// One line, one segment per range-filterable channel (<see
    /// cref="ChatListener.RangeChannelColorOptions"/>), each showing plain unformatted text dimmed
    /// from that channel's own configured chat color via <see
    /// cref="ChatListener.ResolveChannelColorWithSource"/> (passing <c>liveChatTwoRead: true</c> so
    /// a Chat 2 color edited moments ago shows up immediately, unlike production's cached read) —
    /// proves Yell/Shout/Emote keep their own hue instead of falling back to a shared grey, and
    /// labels which tier won (Chat 2's own customized color, vanilla's Log Text Color, or the
    /// last-resort fallback grey) so the priority is directly verifiable without inspecting Chat
    /// 2's config file by hand.
    /// </summary>
    private void PrintChannelNativeFade(int step)
    {
        var builder = new SeStringBuilder();
        builder.Append("Unformatted text per channel - ");

        var first = true;
        foreach (var channel in ChatListener.RangeChannelColorOptions.Keys)
        {
            if (!first)
                builder.Append(" | ");
            first = false;

            var (native, source) = plugin.ChatListener.ResolveChannelColorWithSource(channel, liveChatTwoRead: true);
            AppendFadeSegment(builder, (native, 0u), step, $"{channel} ({source})");
        }

        Plugin.ChatGui.Print(builder.ToReadOnlySeString().ToDalamudString());
    }

    private static void PrintFromTo(string label, (uint Foreground, uint Glow) style, int step)
    {
        var dimmed = UiColorDimmer.DimRgba(style.Foreground, step);

        var builder = new SeStringBuilder();
        builder.Append($"{label}: ");
        AppendFadeSegment(builder, style, 0, $"0x{style.Foreground:X8}");
        builder.Append(" -> ");
        AppendFadeSegment(builder, style, step, $"0x{dimmed:X8}");
        Plugin.ChatGui.Print(builder.ToReadOnlySeString().ToDalamudString());
    }

    /// <summary>One outcome line/cell: full, hidden, or the fade step in its actual color.</summary>
    private void DrawOutcome(int visibility)
    {
        if (visibility == RangeFade.MaxVisibility)
        {
            ImGui.TextUnformatted("100% — fully visible");
            return;
        }

        var step = ChatListener.ResolveFadeStep(visibility);

        if (visibility == 0)
        {
            ImGui.TextColored(stepPreviewColors[step],
                "0% — beyond cut-off: darkest step (Vanilla never removes; Chat 2 may hide render-only)");
            return;
        }

        ImGui.TextColored(stepPreviewColors[step],
            $"{visibility}% — fade step {step} (reference shade — real channels keep their own hue, see test messages below)");
    }
}
#endif
