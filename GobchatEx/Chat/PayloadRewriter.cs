using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Mechanically translates segmentation spans into a new payload list
/// (pattern from ChatAlerts' ChatWatcher.HandleAlert): non-text payloads are
/// copied through by reference, text runs are split per span, and every
/// colored span emits its own balanced UIForeground/UIGlow on/off pair so
/// colors never bracket foreign payloads or leak into following lines.
/// All decisions (what is typed, which color applies) are made by callers.
/// </summary>
internal static class PayloadRewriter
{
    /// <summary>
    /// Builds the rewritten payload list. <paramref name="runPayloadIndices"/>,
    /// <paramref name="runTexts"/> and <paramref name="runSpans"/> are
    /// parallel lists describing the TextPayload runs of the message.
    /// Styles map a segment type to UIColor rows; (0, 0) renders plain.
    /// </summary>
    public static List<Payload> Rewrite(
        IReadOnlyList<Payload> payloads,
        IReadOnlyList<int> runPayloadIndices,
        IReadOnlyList<string> runTexts,
        IReadOnlyList<IReadOnlyList<SegmentSpan>> runSpans,
        IReadOnlyDictionary<SegmentType, (ushort Foreground, ushort Glow)> styles)
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

            AppendRun(result, payloads[i], runTexts[run], runSpans[run], styles);
            ++run;
        }

        return result;
    }

    private static void AppendRun(
        List<Payload> result,
        Payload original,
        string text,
        IReadOnlyList<SegmentSpan> spans,
        IReadOnlyDictionary<SegmentType, (ushort Foreground, ushort Glow)> styles)
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

            if (style.Foreground != 0)
                result.Add(new UIForegroundPayload(style.Foreground));
            if (style.Glow != 0)
                result.Add(new UIGlowPayload(style.Glow));
            result.Add(new TextPayload(sub));
            if (style.Glow != 0)
                result.Add(UIGlowPayload.UIGlowOff);
            if (style.Foreground != 0)
                result.Add(UIForegroundPayload.UIForegroundOff);
        }
    }
}
