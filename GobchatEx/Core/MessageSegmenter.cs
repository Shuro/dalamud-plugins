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

    public MessageSegmenter(IReadOnlyList<TokenRule> rules, IEnumerable<string> mentionTriggers)
        : this(rules, new MentionRules([.. mentionTriggers], [], [], FuzzyMatchLevel.Conservative))
    {
    }

    public MessageSegmenter(IReadOnlyList<TokenRule> rules, MentionRules mentionRules)
    {
        _parsers = rules.Select(rule => new SegmentParser(rule)).ToList();
        _mentions = new MentionMatcher(mentionRules);
    }

    /// <summary>
    /// Segments the text runs of one message. Returns null when nothing
    /// matched, so callers can leave the message untouched (fast path).
    /// </summary>
    public SegmentationResult? Segment(IReadOnlyList<string> runTexts)
    {
        if (runTexts.Count == 0)
            return null;

        var spans = InitialSpans(runTexts);
        foreach (var parser in _parsers)
            spans = parser.Apply(runTexts, spans);

        var hasMention = false;
        if (_mentions.HasTriggers)
        {
            var overlaid = new List<IReadOnlyList<SegmentSpan>>(spans.Count);
            for (var run = 0; run < runTexts.Count; ++run)
            {
                var mentions = _mentions.FindMentions(runTexts[run]);
                hasMention |= mentions.Count > 0;
                overlaid.Add(mentions.Count > 0 ? Overlay(spans[run], mentions) : spans[run]);
            }

            spans = overlaid;
        }

        var anyTyped = spans.Any(runSpans => runSpans.Any(s => s.Type != SegmentType.Undefined));
        return anyTyped ? new SegmentationResult(spans, hasMention) : null;
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
                AddMention(result, cursor, overlapEnd);
                cursor = overlapEnd;
            }
        }

        return result;
    }

    /// <summary>
    /// Appends a Mention span, coalescing with a directly preceding Mention
    /// (a single mention interval can cross span boundaries and would
    /// otherwise emit fragmented adjacent spans).
    /// </summary>
    private static void AddMention(List<SegmentSpan> result, int start, int end)
    {
        if (result.Count > 0 && result[^1] is { Type: SegmentType.Mention } previous && previous.End == start)
            result[^1] = new SegmentSpan(previous.Start, end - previous.Start, SegmentType.Mention);
        else
            result.Add(new SegmentSpan(start, end - start, SegmentType.Mention));
    }
}
