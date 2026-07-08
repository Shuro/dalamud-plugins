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
/// The words a <see cref="MentionMatcher"/> should look for, split by matching strategy:
/// <see cref="WholeWords"/> match whole-word, <see cref="PartialWords"/> as substrings, and
/// <see cref="FuzzyWords"/> are matched per-token within <paramref name="FuzzyLevel"/>'s edit-distance
/// budget (see <see cref="StringSimilarity.MaxDistanceFor"/>).
/// </summary>
public sealed record MentionRules(
    IReadOnlyList<string> WholeWords,
    IReadOnlyList<string> PartialWords,
    IReadOnlyList<string> FuzzyWords,
    FuzzyMatchLevel FuzzyLevel);

/// <summary>
/// Finds configured mention words in run text. Whole words match case-insensitively with word
/// boundaries (lookaround, so triggers ending in punctuation still bound correctly); partial words
/// match as case-insensitive substrings; fuzzy words are matched per word-token within an edit-distance
/// budget (typo tolerance), ported from Gobchat's ReplaceTypeByFuzzyText. Matching runs against an
/// NFKC-normalized copy of the text so decorative "fancy font" code points still match plain-ASCII
/// words, then maps hits back to the original text — ported from Gobchat's ReplaceTypeByText/
/// ReplaceTypeByFuzzyText. Overlapping or touching matches (from any strategy) are merged into single
/// intervals. Regexes and normalized word lists are built once in the constructor; rebuild the matcher
/// on config change, never per message.
/// </summary>
public sealed class MentionMatcher
{
    // Letters, combining marks, and apostrophes (straight + curly) make up a name token; FFXIV names
    // contain apostrophes ("Khit'to"), so they must stay part of the token rather than split it.
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{M}'’]+", RegexOptions.Compiled);

    private readonly IReadOnlyList<Regex> _wholePatterns;
    private readonly IReadOnlyList<Regex> _partialPatterns;
    private readonly IReadOnlyList<string> _fuzzyWords; // pre-normalized: NFKC-folded, apostrophe-folded, lowercased
    private readonly FuzzyMatchLevel _fuzzyLevel;

    public MentionMatcher(MentionRules rules)
    {
        _wholePatterns = BuildPatterns(rules.WholeWords, wholeWord: true);
        _partialPatterns = BuildPatterns(rules.PartialWords, wholeWord: false);
        _fuzzyWords = rules.FuzzyWords
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .Select(NormalizeFuzzyToken)
            .Distinct(StringComparer.Ordinal) // already lowercased, so ordinal dedupe is exact
            .ToList();
        _fuzzyLevel = rules.FuzzyLevel;
    }

    /// <summary>False when no usable (non-empty) word is configured, in any list.</summary>
    public bool HasTriggers => _wholePatterns.Count > 0 || _partialPatterns.Count > 0 || _fuzzyWords.Count > 0;

    /// <summary>
    /// Returns sorted, merged, non-overlapping Mention spans for all configured word matches in
    /// <paramref name="text"/>; empty when nothing matches.
    /// </summary>
    public IReadOnlyList<SegmentSpan> FindMentions(string text)
    {
        if (!HasTriggers || text.Length == 0)
            return [];

        var (matchText, map) = UnicodeNormalizer.NormalizeWithMap(text);

        List<(int Start, int End)>? intervals = null;
        void AddMatches(IEnumerable<Regex> patterns)
        {
            foreach (var pattern in patterns)
            {
                for (var match = pattern.Match(matchText); match.Success; match = match.NextMatch())
                {
                    intervals ??= [];
                    intervals.Add((match.Index, match.Index + match.Length));
                }
            }
        }

        AddMatches(_wholePatterns);
        AddMatches(_partialPatterns);

        if (_fuzzyWords.Count > 0)
        {
            foreach (Match token in TokenRegex.Matches(matchText))
            {
                if (!IsFuzzyMatch(NormalizeFuzzyToken(token.Value)))
                    continue;
                intervals ??= [];
                intervals.Add((token.Index, token.Index + token.Length));
            }
        }

        if (intervals == null)
            return [];

        if (map != null)
            for (var i = 0; i < intervals.Count; ++i)
                intervals[i] = (map[intervals[i].Start], map[intervals[i].End]);

        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        return MergeIntervals(intervals);
    }

    private bool IsFuzzyMatch(string token)
    {
        var tokenLength = token.Length;
        foreach (var word in _fuzzyWords)
        {
            var budget = StringSimilarity.MaxDistanceFor(_fuzzyLevel, word.Length);
            if (budget < 0)
                continue; // word too short to fuzzy-match safely
            if (Math.Abs(tokenLength - word.Length) > budget)
                continue; // length gap alone already exceeds the budget
            if (StringSimilarity.OsaDistance(token, word) <= budget)
                return true;
        }
        return false;
    }

    // NFKC-fold (so decorative code points like math-bold "𝗙𝗟𝗨𝗫" collapse to "FLUX"), fold the curly
    // apostrophe onto the straight one, and lowercase — so fuzzy matching is case-, decoration-, and
    // apostrophe-style-insensitive on both the words and the message tokens.
    private static string NormalizeFuzzyToken(string value)
        => UnicodeNormalizer.Normalize(value).Replace('’', '\'').ToLowerInvariant();

    private static IReadOnlyList<Regex> BuildPatterns(IEnumerable<string> words, bool wholeWord)
        => words
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(w =>
            {
                var escaped = Regex.Escape(w);
                var pattern = wholeWord ? $@"(?<!\w){escaped}(?!\w)" : escaped;
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            })
            .ToList();

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
