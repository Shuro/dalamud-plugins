using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Mechanically translates segmentation spans into a new payload list
/// (pattern from ChatAlerts' ChatWatcher.HandleAlert): non-text payloads are
/// copied through by reference, text runs are split per span, and every
/// colored span emits its own balanced raw Color/EdgeColor push/pop pair
/// (<see cref="SeStringColorMacro"/>) so colors never bracket foreign
/// payloads or leak into following lines. All decisions (what is typed,
/// which color applies) are made by callers.
/// </summary>
internal static class PayloadRewriter
{
    /// <summary>
    /// Builds the rewritten payload list. <paramref name="runPayloadIndices"/>,
    /// <paramref name="runTexts"/> and <paramref name="runSpans"/> are
    /// parallel lists describing the TextPayload runs of the message.
    /// Styles map a segment type to packed RGBA colors; (0, 0) renders plain.
    /// <paramref name="fadeStep"/>, when set, pre-dims each color via
    /// <see cref="UiColorDimmer.DimRgba"/> before it's emitted.
    /// </summary>
    public static List<Payload> Rewrite(
        IReadOnlyList<Payload> payloads,
        IReadOnlyList<int> runPayloadIndices,
        IReadOnlyList<string> runTexts,
        IReadOnlyList<IReadOnlyList<SegmentSpan>> runSpans,
        IReadOnlyDictionary<SegmentType, (uint Foreground, uint Glow)> styles,
        int? fadeStep = null)
    {
        var result = new List<Payload>(payloads.Count + (4 * runSpans.Count));
        var run = 0;
        for (var i = 0; i < payloads.Count; ++i)
        {
            if (run >= runPayloadIndices.Count || runPayloadIndices[run] != i)
            {
                result.Add(payloads[i]);
                continue;
            }

            AppendRun(result, payloads[i], runTexts[run], runSpans[run], styles, fadeStep);
            ++run;
        }

        return result;
    }

    /// <summary>
    /// Builds the rewritten payload list for a run set that all shares one color, e.g. a sender name
    /// (one color for the whole name, not the multiple <see cref="SegmentType"/>s <see cref="Rewrite"/>
    /// handles). Each run in <paramref name="runPayloadIndices"/> is wrapped as a whole; payloads outside
    /// the run set (e.g. a cross-world icon payload between name runs) pass through untouched, exactly
    /// like <see cref="Rewrite"/>.
    /// </summary>
    public static List<Payload> RewriteUniform(
        IReadOnlyList<Payload> payloads,
        IReadOnlyList<int> runPayloadIndices,
        IReadOnlyList<string> runTexts,
        (uint Foreground, uint Glow) style,
        int? fadeStep = null)
    {
        var result = new List<Payload>(payloads.Count + (4 * runPayloadIndices.Count));
        var run = 0;
        for (var i = 0; i < payloads.Count; ++i)
        {
            if (run >= runPayloadIndices.Count || runPayloadIndices[run] != i)
            {
                result.Add(payloads[i]);
                continue;
            }

            if (style.Foreground == 0 && style.Glow == 0)
                result.Add(new TextPayload(runTexts[run]));
            else
                AppendColored(result, runTexts[run], style, fadeStep);
            ++run;
        }

        return result;
    }

    private static void AppendRun(
        List<Payload> result,
        Payload original,
        string text,
        IReadOnlyList<SegmentSpan> spans,
        IReadOnlyDictionary<SegmentType, (uint Foreground, uint Glow)> styles,
        int? fadeStep)
    {
        // Untouched run: keep the original payload instead of re-creating it.
        if (spans.Count == 1 && spans[0].Type == SegmentType.Undefined)
        {
            result.Add(original);
            return;
        }

        foreach (var span in spans)
        {
            var sub = text.Substring(span.Start, span.Length);
            if (span.Type == SegmentType.Undefined
                || !styles.TryGetValue(span.Type, out var style)
                || (style.Foreground == 0 && style.Glow == 0))
            {
                result.Add(new TextPayload(sub));
                continue;
            }

            AppendColored(result, sub, style, fadeStep);
        }
    }

    private static void AppendColored(
        List<Payload> result, string text, (uint Foreground, uint Glow) style, int? fadeStep)
    {
        var foreground = fadeStep is { } fgStep ? UiColorDimmer.DimRgba(style.Foreground, fgStep) : style.Foreground;
        var glow = fadeStep is { } glowStep ? UiColorDimmer.DimRgba(style.Glow, glowStep) : style.Glow;

        if (foreground != 0)
            result.Add(SeStringColorMacro.MakeColorMacro(SeStringColorMacro.ColorMacroCode, SeStringColorMacro.ToOpaqueAarrggbb(foreground)));
        if (glow != 0)
            result.Add(SeStringColorMacro.MakeColorMacro(SeStringColorMacro.EdgeColorMacroCode, SeStringColorMacro.ToOpaqueAarrggbb(glow)));
        result.Add(new TextPayload(text));
        if (glow != 0)
            result.Add(SeStringColorMacro.PopEdgeColor());
        if (foreground != 0)
            result.Add(SeStringColorMacro.PopColor());
    }
}
