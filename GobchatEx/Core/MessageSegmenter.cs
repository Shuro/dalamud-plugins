using System;
using System.Collections.Generic;
using System.Linq;

namespace GobchatEx.Core;

/// <summary>
/// Segmentation output: one span list per input run (each tiling its run
/// text), plus whether any mention trigger matched.
/// </summary>
public sealed record SegmentationResult(
    IReadOnlyList<IReadOnlyList<SegmentSpan>> RunSpans,
    bool HasMention);

/// <summary>
/// Orchestrates the segmentation pipeline for one message: token-rule passes
/// in precedence order, then the mention overlay on top. Construct once per
/// settings change; <see cref="Segment"/> is stateless per message.
/// </summary>
public sealed class MessageSegmenter
{
    private readonly IReadOnlyList<SegmentParser> _parsers;
    private readonly MentionMatcher _mentions;

    public MessageSegmenter(IReadOnlyList<TokenRule> rules, MentionRules mentionRules)
    {
        _parsers = rules.Select(rule => new SegmentParser(rule)).ToList();
        _mentions = new MentionMatcher(mentionRules);
    }

    /// <summary>
    /// Segments the text runs of one message. Returns null when nothing
    /// matched, so callers can leave the message untouched (fast path).
    /// <paramref name="defaultType"/>, when not <see cref="SegmentType.Undefined"/>, is applied to
    /// whatever text the token rules and mention overlay left untyped — e.g. a plain, unquoted
    /// /say line still renders as Say once the channel implies it.
    /// <paramref name="overlayMentions"/>, when false, still matches mentions (HasMention keeps
    /// driving the sound decision) but skips the visual overlay — the own-message highlight
    /// suppression path; a mention-only result then carries all-Undefined spans, which the
    /// payload rewriter passes through untouched.
    /// <paramref name="detectEmote"/> is the app's "autodetect emote" rule: a quoted (Say) span
    /// anywhere in the message reclassifies everything still untyped as Emote. Runs after the
    /// mention overlay (mentions are never overwritten) and before <paramref name="defaultType"/>,
    /// so a /say leftover becomes Emote instead of Say.
    /// </summary>
    public SegmentationResult? Segment(
        IReadOnlyList<string> runTexts,
        SegmentType defaultType = SegmentType.Undefined,
        bool overlayMentions = true,
        bool detectEmote = false)
    {
        if (runTexts.Count == 0)
            return null;

        var spans = InitialSpans(runTexts);
        foreach (var parser in _parsers)
            spans = parser.Apply(runTexts, spans);

        var hasMention = false;
        if (_mentions.HasTriggers)
        {
            // Mentions are matched per run, in isolation — unlike the token-rule passes, where
            // SegmentParser carries open-delimiter state across runs. A trigger word split across
            // two runs (rare: runs only break where a non-text payload interrupts the text)
            // won't match.
            var overlaid = new List<IReadOnlyList<SegmentSpan>>(spans.Count);
            for (var run = 0; run < runTexts.Count; ++run)
            {
                var mentions = _mentions.FindMentions(runTexts[run]);
                hasMention |= mentions.Count > 0;
                overlaid.Add(overlayMentions && mentions.Count > 0 ? Overlay(spans[run], mentions) : spans[run]);
            }

            spans = overlaid;
        }

        if (detectEmote && spans.Any(runSpans => runSpans.Any(s => s.Type == SegmentType.Say)))
            spans = ApplyDefaultType(spans, SegmentType.Emote);

        if (defaultType != SegmentType.Undefined)
            spans = ApplyDefaultType(spans, defaultType);

        // hasMention implies anyTyped while the overlay runs; with it skipped, a mention-only
        // message must still produce a result or the mention would vanish for the sound path.
        var anyTyped = spans.Any(runSpans => runSpans.Any(s => s.Type != SegmentType.Undefined));
        return anyTyped || hasMention ? new SegmentationResult(spans, hasMention) : null;
    }

    /// <summary>Recolors any still-Undefined span to <paramref name="defaultType"/>, leaving typed spans as-is.</summary>
    private static List<IReadOnlyList<SegmentSpan>> ApplyDefaultType(
        IReadOnlyList<IReadOnlyList<SegmentSpan>> spans, SegmentType defaultType)
    {
        var result = new List<IReadOnlyList<SegmentSpan>>(spans.Count);
        foreach (var runSpans in spans)
        {
            result.Add(runSpans.Any(s => s.Type == SegmentType.Undefined)
                ? runSpans.Select(s => s.Type == SegmentType.Undefined ? s with { Type = defaultType } : s).ToList()
                : runSpans);
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<SegmentSpan>> InitialSpans(IReadOnlyList<string> runTexts)
        => runTexts
            .Select(IReadOnlyList<SegmentSpan> (text) => text.Length > 0
                ? [new SegmentSpan(0, text.Length, SegmentType.Undefined)]
                : [])
            .ToList();

    /// <summary>
    /// Splits the tiling span list wherever a mention interval intersects it;
    /// intersected portions become Mention (mentions recolor on top of every
    /// segment type). Both inputs are ordered; output tiles the same range.
    /// </summary>
    private static List<SegmentSpan> Overlay(
        IReadOnlyList<SegmentSpan> spans, IReadOnlyList<SegmentSpan> mentions)
    {
        var result = new List<SegmentSpan>(spans.Count + (2 * mentions.Count));
        var m = 0;
        foreach (var span in spans)
        {
            var cursor = span.Start;
            while (cursor < span.End)
            {
                while (m < mentions.Count && mentions[m].End <= cursor)
                    ++m;

                if (m >= mentions.Count || mentions[m].Start >= span.End)
                {
                    result.Add(new SegmentSpan(cursor, span.End - cursor, span.Type));
                    break;
                }

                if (mentions[m].Start > cursor)
                {
                    result.Add(new SegmentSpan(cursor, mentions[m].Start - cursor, span.Type));
                    cursor = mentions[m].Start;
                }

                var overlapEnd = Math.Min(mentions[m].End, span.End);
                AddMention(result, cursor, overlapEnd, mentions[m].StyleId);
                cursor = overlapEnd;
            }
        }

        return result;
    }

    /// <summary>
    /// Appends a Mention span, coalescing with a directly preceding Mention only when the style
    /// matches (a single mention interval can cross span boundaries and would otherwise emit
    /// fragmented adjacent spans; two differently-styled mentions that merely touch must stay
    /// separate spans so each keeps its own color).
    /// </summary>
    private static void AddMention(List<SegmentSpan> result, int start, int end, int styleId)
    {
        if (result.Count > 0 && result[^1] is { Type: SegmentType.Mention } previous
            && previous.End == start && previous.StyleId == styleId)
            result[^1] = new SegmentSpan(previous.Start, end - previous.Start, SegmentType.Mention, styleId);
        else
            result.Add(new SegmentSpan(start, end - start, SegmentType.Mention, styleId));
    }
}
