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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GobchatEx.Core;

/// <summary>
/// Finds configured mention trigger words in run text: case-insensitive,
/// whole-word (lookaround boundaries, so triggers ending in punctuation still
/// bound correctly). Overlapping or touching matches are merged into single
/// intervals, ported from Gobchat's ReplaceTypeByText merge logic.
/// Regexes are compiled once in the constructor; rebuild the matcher on
/// config change, never per message.
/// </summary>
public sealed class MentionMatcher
{
    private readonly IReadOnlyList<Regex> _patterns;

    public MentionMatcher(IEnumerable<string> triggers)
    {
        _patterns = triggers
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => new Regex(
                $@"(?<!\w){Regex.Escape(t)}(?!\w)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
    }

    /// <summary>False when no usable (non-empty) trigger is configured.</summary>
    public bool HasTriggers => _patterns.Count > 0;

    /// <summary>
    /// Returns sorted, merged, non-overlapping Mention spans for all trigger
    /// matches in <paramref name="text"/>; empty when nothing matches.
    /// </summary>
    public IReadOnlyList<SegmentSpan> FindMentions(string text)
    {
        if (_patterns.Count == 0 || text.Length == 0)
            return [];

        List<(int Start, int End)>? intervals = null;
        foreach (var pattern in _patterns)
        {
            for (var match = pattern.Match(text); match.Success; match = match.NextMatch())
            {
                intervals ??= [];
                intervals.Add((match.Index, match.Index + match.Length));
            }
        }

        if (intervals == null)
            return [];

        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        return MergeIntervals(intervals);
    }

    private static List<SegmentSpan> MergeIntervals(List<(int Start, int End)> sorted)
    {
        var result = new List<SegmentSpan>(sorted.Count);
        var (start, end) = sorted[0];
        foreach (var interval in sorted.Skip(1))
        {
            if (interval.Start <= end)
            {
                end = Math.Max(end, interval.End); // overlap or touch: extend
                continue;
            }

            result.Add(new SegmentSpan(start, end - start, SegmentType.Mention));
            (start, end) = interval;
        }

        result.Add(new SegmentSpan(start, end - start, SegmentType.Mention));
        return result;
    }
}
