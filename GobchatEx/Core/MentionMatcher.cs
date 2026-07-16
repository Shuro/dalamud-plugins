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
/// One mention word plus the per-word color override it should carry (0 = none, falls back to the
/// default mention style at render time). Implicitly convertible from a plain string (StyleId 0)
/// so unstyled call sites and tests can keep passing string collections directly.
/// </summary>
public readonly record struct MentionWord(string Word, int StyleId = 0)
{
    public static implicit operator MentionWord(string word) => new(word);
}

/// <summary>
/// The words a <see cref="MentionMatcher"/> should look for, split by matching strategy:
/// <see cref="WholeWords"/> match whole-word, <see cref="PartialWords"/> as substrings, and
/// <see cref="FuzzyWords"/> are matched per-token within <paramref name="FuzzyLevel"/>'s edit-distance
/// budget (see <see cref="StringSimilarity.MaxDistanceFor"/>). <paramref name="Styles"/> is the
/// per-word color-override table each word's <see cref="MentionWord.StyleId"/> indexes into
/// (id - 1); null/empty means no word carries an override.
/// </summary>
public sealed record MentionRules(
    IReadOnlyList<MentionWord> WholeWords,
    IReadOnlyList<MentionWord> PartialWords,
    IReadOnlyList<MentionWord> FuzzyWords,
    FuzzyMatchLevel FuzzyLevel,
    IReadOnlyList<(uint Foreground, uint Glow)>? Styles = null);

/// <summary>
/// Finds configured mention words in run text. Whole words match case-insensitively with word
/// boundaries (lookaround, so triggers ending in punctuation still bound correctly); partial words
/// match as case-insensitive substrings; fuzzy words are matched per word-token within an edit-distance
/// budget (typo tolerance), ported from Gobchat's ReplaceTypeByFuzzyText. Matching runs against an
/// NFKC-normalized copy of the text so decorative "fancy font" code points still match plain-ASCII
/// words, then maps hits back to the original text — ported from Gobchat's ReplaceTypeByText/
/// ReplaceTypeByFuzzyText. Regexes and normalized word lists are built once in the constructor;
/// rebuild the matcher on config change, never per message.
///
/// Overlapping or touching matches sharing the same style merge into one interval, exactly as
/// before per-word styling existed. Matches with different styles never merge — instead the
/// highest-priority match wins the contested text and the loser keeps only its remainder (see
/// <see cref="MergeIntervals"/>) — so total mention coverage stays the same union of matches,
/// only the color boundaries change.
/// </summary>
public sealed class MentionMatcher
{
    // Letters, combining marks, and apostrophes (straight + curly) make up a name token; FFXIV names
    // contain apostrophes ("Khit'to"), so they must stay part of the token rather than split it.
    // Format (Cf) code points — zero-width space/joiner and kin — are neither part of a token here
    // nor removed by UnicodeNormalizer's NFKC fold, so one embedded in a word splits the token and
    // defeats whole-word and fuzzy matching alike. Accepted limitation: stripping Cf would
    // complicate the normalizer's index map for input that hasn't shown up in practice.
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{M}'’]+", RegexOptions.Compiled);

    private readonly IReadOnlyList<(Regex Pattern, int StyleId)> _wholePatterns;
    private readonly IReadOnlyList<(Regex Pattern, int StyleId)> _partialPatterns;
    private readonly IReadOnlyList<(string Normalized, int StyleId)> _fuzzyWords; // pre-normalized: NFKC-folded, apostrophe-folded, lowercased
    private readonly FuzzyMatchLevel _fuzzyLevel;

    public MentionMatcher(MentionRules rules)
    {
        _wholePatterns = BuildPatterns(rules.WholeWords, wholeWord: true);
        _partialPatterns = BuildPatterns(rules.PartialWords, wholeWord: false);
        _fuzzyWords = BuildFuzzyWords(rules.FuzzyWords);
        _fuzzyLevel = rules.FuzzyLevel;
    }

    /// <summary>False when no usable (non-empty) word is configured, in any list.</summary>
    public bool HasTriggers => _wholePatterns.Count > 0 || _partialPatterns.Count > 0 || _fuzzyWords.Count > 0;

    /// <summary>
    /// Returns sorted, non-overlapping Mention spans (each carrying its resolved
    /// <see cref="SegmentSpan.StyleId"/>) for all configured word matches in <paramref name="text"/>;
    /// empty when nothing matches.
    /// </summary>
    public IReadOnlyList<SegmentSpan> FindMentions(string text)
    {
        if (!HasTriggers || text.Length == 0)
            return [];

        var (matchText, map) = UnicodeNormalizer.NormalizeWithMap(text);

        List<(int Start, int End, int StyleId)>? intervals = null;
        void AddMatches(IEnumerable<(Regex Pattern, int StyleId)> patterns)
        {
            foreach (var (pattern, styleId) in patterns)
            {
                for (var match = pattern.Match(matchText); match.Success; match = match.NextMatch())
                {
                    intervals ??= [];
                    intervals.Add((match.Index, match.Index + match.Length, styleId));
                }
            }
        }

        AddMatches(_wholePatterns);
        AddMatches(_partialPatterns);

        if (_fuzzyWords.Count > 0)
        {
            foreach (Match token in TokenRegex.Matches(matchText))
            {
                if (!TryFuzzyMatch(NormalizeFuzzyToken(token.Value), out var styleId))
                    continue;
                intervals ??= [];
                intervals.Add((token.Index, token.Index + token.Length, styleId));
            }
        }

        if (intervals == null)
            return [];

        if (map != null)
            for (var i = 0; i < intervals.Count; ++i)
                intervals[i] = (map[intervals[i].Start], map[intervals[i].End], intervals[i].StyleId);

        intervals.Sort(ComparePriority);
        return MergeIntervals(intervals);
    }

    /// <summary>
    /// Total order matches are merged/resolved in: earlier start first; on a start tie, the longer
    /// match first (it fully covers the shorter one); on a start+length tie, an explicit per-word
    /// override beats the default (unstyled) style; on a full tie, the lower style id — i.e. the
    /// earlier-allocated, more config-order-significant word — wins. Deterministic regardless of
    /// original match/regex order.
    /// </summary>
    private static int ComparePriority((int Start, int End, int StyleId) a, (int Start, int End, int StyleId) b)
    {
        var byStart = a.Start.CompareTo(b.Start);
        if (byStart != 0)
            return byStart;

        var byLength = (b.End - b.Start).CompareTo(a.End - a.Start);
        if (byLength != 0)
            return byLength;

        var aStyled = a.StyleId != 0;
        var bStyled = b.StyleId != 0;
        if (aStyled != bStyled)
            return aStyled ? -1 : 1;

        return a.StyleId.CompareTo(b.StyleId);
    }

    private bool TryFuzzyMatch(string token, out int styleId)
    {
        var tokenLength = token.Length;
        foreach (var (word, id) in _fuzzyWords)
        {
            var budget = StringSimilarity.MaxDistanceFor(_fuzzyLevel, word.Length);
            if (budget < 0)
                continue; // word too short to fuzzy-match safely
            if (Math.Abs(tokenLength - word.Length) > budget)
                continue; // length gap alone already exceeds the budget
            if (StringSimilarity.OsaDistance(token, word) <= budget)
            {
                styleId = id;
                return true;
            }
        }

        styleId = 0;
        return false;
    }

    // NFKC-fold (so decorative code points like math-bold "𝗙𝗟𝗨𝗫" collapse to "FLUX"), fold the curly
    // apostrophe onto the straight one, and lowercase — so fuzzy matching is case-, decoration-, and
    // apostrophe-style-insensitive on both the words and the message tokens.
    private static string NormalizeFuzzyToken(string value)
        => UnicodeNormalizer.Normalize(value).Replace('’', '\'').ToLowerInvariant();

    /// <summary>Trims and drops empty words, first-wins case-insensitive dedupe (keeps the first
    /// occurrence's style, mirroring the pre-styling <c>Distinct(OrdinalIgnoreCase)</c> behavior).</summary>
    private static IReadOnlyList<(Regex Pattern, int StyleId)> BuildPatterns(IEnumerable<MentionWord> words, bool wholeWord)
    {
        var styleById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var word in words)
        {
            var trimmed = word.Word?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || !styleById.TryAdd(trimmed, word.StyleId))
                continue;
            order.Add(trimmed);
        }

        return order.Select(w =>
        {
            var escaped = Regex.Escape(w);
            var pattern = wholeWord ? $@"(?<!\w){escaped}(?!\w)" : escaped;
            return (new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), styleById[w]);
        }).ToList();
    }

    /// <summary>Same first-wins dedupe as <see cref="BuildPatterns"/>, keyed by the normalized token.</summary>
    private static IReadOnlyList<(string Normalized, int StyleId)> BuildFuzzyWords(IEnumerable<MentionWord> words)
    {
        var styleById = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var word in words)
        {
            var trimmed = word.Word?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
                continue;
            var normalized = NormalizeFuzzyToken(trimmed);
            if (!styleById.TryAdd(normalized, word.StyleId))
                continue;
            order.Add(normalized);
        }

        return order.Select(w => (w, styleById[w])).ToList();
    }

    /// <summary>
    /// Sweeps the priority-sorted intervals into non-overlapping spans. A run extends while later
    /// intervals share its style and overlap/touch it; a different-style interval only contributes
    /// its remainder beyond the current run's end (the current run already won the contested part,
    /// per <see cref="ComparePriority"/>) — dropped entirely when fully covered.
    /// </summary>
    private static List<SegmentSpan> MergeIntervals(List<(int Start, int End, int StyleId)> sorted)
    {
        var result = new List<SegmentSpan>(sorted.Count);
        var (start, end, styleId) = sorted[0];
        foreach (var interval in sorted.Skip(1))
        {
            if (interval.Start > end)
            {
                result.Add(new SegmentSpan(start, end - start, SegmentType.Mention, styleId));
                (start, end, styleId) = interval;
                continue;
            }

            if (interval.StyleId == styleId)
            {
                end = Math.Max(end, interval.End);
                continue;
            }

            if (interval.End <= end)
                continue; // fully covered by the higher-priority run: drop

            result.Add(new SegmentSpan(start, end - start, SegmentType.Mention, styleId));
            (start, end, styleId) = (end, interval.End, interval.StyleId);
        }

        result.Add(new SegmentSpan(start, end - start, SegmentType.Mention, styleId));
        return result;
    }
}
