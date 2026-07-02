/*******************************************************************************
 * Copyright (C) 2019-2025 MarbleBag
 * Copyright (C) 2026 Shuro
 *
 * This program is free software: you can redistribute it and/or modify it under
 * the terms of the GNU Affero General Public License as published by the Free
 * Software Foundation, version 3.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>
 *
 * SPDX-License-Identifier: AGPL-3.0-only
 *******************************************************************************/

using System.Collections.Generic;

namespace GobchatEx.Core;

/// <summary>
/// Applies a single <see cref="TokenRule"/> pass over the text runs of one
/// message. Ported from Gobchat's ReplaceTypeByToken: only Undefined spans
/// are subdivided, the delimiter open-state carries across runs and across
/// already-typed spans, an unclosed delimiter marks text to the end of the
/// message, and there is no nesting.
/// </summary>
public sealed class SegmentParser
{
    private readonly TokenRule _rule;

    public SegmentParser(TokenRule rule) => _rule = rule;

    /// <summary>
    /// Runs one pass over all runs of a message and returns new span lists;
    /// the input lists are not modified. Delimiter state is local to the
    /// call, so state never leaks between messages.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<SegmentSpan>> Apply(
        IReadOnlyList<string> runTexts,
        IReadOnlyList<IReadOnlyList<SegmentSpan>> runSpans)
    {
        var result = new List<IReadOnlyList<SegmentSpan>>(runTexts.Count);
        var open = false;
        for (var run = 0; run < runTexts.Count; ++run)
            result.Add(ApplyToRun(runTexts[run], runSpans[run], ref open));
        return result;
    }

    private List<SegmentSpan> ApplyToRun(string text, IReadOnlyList<SegmentSpan> spans, ref bool open)
    {
        var result = new List<SegmentSpan>(spans.Count);
        foreach (var span in spans)
        {
            // Typed spans are kept as-is; the open-state persists across them
            // (a quote opened before an OOC block closes after it).
            if (span.Type != SegmentType.Undefined)
                result.Add(span);
            else
                ScanUndefinedSpan(text, span, result, ref open);
        }

        return result;
    }

    private void ScanUndefinedSpan(string text, SegmentSpan span, List<SegmentSpan> result, ref bool open)
    {
        var end = span.End;
        var segmentStart = span.Start;
        var i = span.Start;
        while (i < end)
        {
            var tokenLength = MatchToken(text, i, end, open ? _rule.EndTokens : _rule.StartTokens);
            if (tokenLength == 0)
            {
                ++i;
                continue;
            }

            if (open)
            {
                // Closing token found; it belongs to the typed span.
                result.Add(new SegmentSpan(segmentStart, i + tokenLength - segmentStart, _rule.Type));
                segmentStart = i + tokenLength;
                open = false;
            }
            else
            {
                // Opening token found; it belongs to the typed span.
                if (i > segmentStart)
                    result.Add(new SegmentSpan(segmentStart, i - segmentStart, SegmentType.Undefined));
                segmentStart = i;
                open = true;
            }

            i += tokenLength;
        }

        if (segmentStart < end)
            result.Add(new SegmentSpan(segmentStart, end - segmentStart, open ? _rule.Type : SegmentType.Undefined));
    }

    private static int MatchToken(string text, int offset, int spanEnd, IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (offset + token.Length <= spanEnd
                && string.CompareOrdinal(text, offset, token, 0, token.Length) == 0)
                return token.Length;
        }

        return 0;
    }
}
