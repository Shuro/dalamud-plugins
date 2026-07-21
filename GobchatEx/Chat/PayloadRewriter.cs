using System.Collections.Generic;
using GobchatEx.Core;
using Lumina.Text;
using Lumina.Text.ReadOnly;

namespace GobchatEx.Chat;

/// <summary>
/// Mechanically translates segmentation spans into a new SeString: streams over the parsed
/// payloads of the source message, copies non-text payloads through unchanged, splits each text
/// run per span, and wraps every colored span in its own balanced Color/EdgeColor push/pop pair
/// (Lumina's <see cref="SeStringBuilder"/> owns the macro + integer-expression encoding) so colors
/// never bracket foreign payloads or leak into following lines. All decisions (what is typed, which
/// color applies) are made by callers. The <paramref name="source"/> is iterated in the same order
/// and with the same "non-empty text payload = one run" rule as
/// <c>ChatListener.CollectTextRuns</c>, so <paramref name="runTexts"/>/<paramref name="runSpans"/>
/// line up positionally with the text payloads.
/// </summary>
internal static class PayloadRewriter
{
    /// <summary>
    /// Builds the rewritten string. Styles map a segment type to packed RGBA colors; (0, 0) renders
    /// plain. <paramref name="mentionOverrides"/> is the per-word color-override table a Mention
    /// span's <see cref="SegmentSpan.StyleId"/> indexes into (see <see cref="MentionStyleResolver"/>);
    /// empty disables all overrides, falling every Mention span back to <paramref name="styles"/>'s
    /// entry. <paramref name="fadeStep"/>, when set, pre-dims each color via
    /// <see cref="UiColorDimmer.DimRgba"/> before it's emitted.
    /// </summary>
    public static ReadOnlySeString Rewrite(
        ReadOnlySeStringSpan source,
        IReadOnlyList<string> runTexts,
        IReadOnlyList<IReadOnlyList<SegmentSpan>> runSpans,
        IReadOnlyDictionary<SegmentType, (uint Foreground, uint Glow)> styles,
        IReadOnlyList<(uint Foreground, uint Glow)> mentionOverrides,
        int? fadeStep = null)
    {
        var builder = new SeStringBuilder();
        var run = 0;
        foreach (var payload in source)
        {
            if (payload.Type == ReadOnlySePayloadType.Text && payload.Body.Length > 0)
            {
                AppendRun(builder, payload, runTexts[run], runSpans[run], styles, mentionOverrides, fadeStep);
                ++run;
            }
            else
            {
                builder.Append(payload);
            }
        }

        return builder.ToReadOnlySeString();
    }

    /// <summary>
    /// Builds the rewritten string for a run set that all shares one color, e.g. a sender name (one
    /// color for the whole name, not the multiple <see cref="SegmentType"/>s <see cref="Rewrite"/>
    /// handles). Each text payload is wrapped as a whole; payloads outside the run set (e.g. a
    /// cross-world icon payload between name runs) pass through untouched, exactly like
    /// <see cref="Rewrite"/>.
    /// </summary>
    public static ReadOnlySeString RewriteUniform(
        ReadOnlySeStringSpan source,
        IReadOnlyList<string> runTexts,
        (uint Foreground, uint Glow) style,
        int? fadeStep = null)
    {
        var builder = new SeStringBuilder();
        var run = 0;
        foreach (var payload in source)
        {
            if (payload.Type == ReadOnlySePayloadType.Text && payload.Body.Length > 0)
            {
                if (style.Foreground == 0 && style.Glow == 0)
                    builder.Append(payload);
                else
                    AppendColored(builder, runTexts[run], style, fadeStep);
                ++run;
            }
            else
            {
                builder.Append(payload);
            }
        }

        return builder.ToReadOnlySeString();
    }

    private static void AppendRun(
        SeStringBuilder builder,
        ReadOnlySePayloadSpan original,
        string text,
        IReadOnlyList<SegmentSpan> spans,
        IReadOnlyDictionary<SegmentType, (uint Foreground, uint Glow)> styles,
        IReadOnlyList<(uint Foreground, uint Glow)> mentionOverrides,
        int? fadeStep)
    {
        // Untouched run: re-append the original text payload verbatim instead of re-creating it.
        if (spans.Count == 1 && spans[0].Type == SegmentType.Undefined)
        {
            builder.Append(original);
            return;
        }

        foreach (var span in spans)
        {
            var sub = text.Substring(span.Start, span.Length);
            if (span.Type == SegmentType.Undefined || !styles.TryGetValue(span.Type, out var style))
            {
                builder.Append(sub);
                continue;
            }

            if (span.Type == SegmentType.Mention && span.StyleId != 0)
                style = MentionStyleResolver.Resolve(span.StyleId, mentionOverrides, style.Foreground, style.Glow);

            if (style.Foreground == 0 && style.Glow == 0)
            {
                builder.Append(sub);
                continue;
            }

            AppendColored(builder, sub, style, fadeStep);
        }
    }

    private static void AppendColored(
        SeStringBuilder builder, string text, (uint Foreground, uint Glow) style, int? fadeStep)
    {
        var foreground = fadeStep is { } fgStep ? UiColorDimmer.DimRgba(style.Foreground, fgStep) : style.Foreground;
        var glow = fadeStep is { } glowStep ? UiColorDimmer.DimRgba(style.Glow, glowStep) : style.Glow;

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
}
