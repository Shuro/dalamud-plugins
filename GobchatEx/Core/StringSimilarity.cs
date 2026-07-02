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

namespace GobchatEx.Core;

/// <summary>
/// How forgiving the per-character fuzzy player-mention matching is. The default is
/// <see cref="Conservative"/> — see <see cref="StringSimilarity.MaxDistanceFor"/> for the actual
/// edit-distance thresholds each level grants per word length.
/// </summary>
public enum FuzzyMatchLevel
{
    Conservative,
    Balanced,
    Aggressive
}

/// <summary>
/// String-distance helpers backing the fuzzy player-mention matcher. The matcher treats a word in
/// a message as a mention when its edit distance to a configured name is within the budget
/// <see cref="MaxDistanceFor"/> grants for that name's length.
/// </summary>
public static class StringSimilarity
{
    /// <summary>
    /// Optimal String Alignment distance (restricted Damerau–Levenshtein): substitution, insertion,
    /// deletion, and <em>adjacent</em> transposition each cost 1. Transposition is what lets a name
    /// like "Khit'to" still match "Kiht'to" at distance 1 (plain Levenshtein would score it 2).
    /// </summary>
    public static int OsaDistance(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        int n = a.Length;
        int m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        // Three rolling rows: row i-2 (for the transposition lookback), row i-1, and the current row.
        var prevPrev = new int[m + 1];
        var prev = new int[m + 1];
        var curr = new int[m + 1];

        for (var j = 0; j <= m; j++)
            prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var deletion = prev[j] + 1;
                var insertion = curr[j - 1] + 1;
                var substitution = prev[j - 1] + cost;
                var value = Math.Min(Math.Min(deletion, insertion), substitution);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, prevPrev[j - 2] + 1);

                curr[j] = value;
            }

            // Rotate the rows; reuse the oldest array as the next "current".
            var recycled = prevPrev;
            prevPrev = prev;
            prev = curr;
            curr = recycled;
        }

        return prev[m];
    }

    /// <summary>
    /// The maximum edit distance a word of <paramref name="wordLength"/> characters is allowed to be
    /// from a message token to still count as a fuzzy match, for the given <paramref name="level"/>.
    /// Returns <c>-1</c> when the word is too short to fuzzy-match safely (exact matching only) — the
    /// guard that stops short names like "Ana" from matching "and"/"any".
    /// </summary>
    public static int MaxDistanceFor(FuzzyMatchLevel level, int wordLength)
    {
        switch (level)
        {
            case FuzzyMatchLevel.Conservative:
                return wordLength >= 5 ? 1 : -1;

            case FuzzyMatchLevel.Aggressive:
                if (wordLength >= 6) return 2;
                if (wordLength >= 4) return 1;
                return -1;

            case FuzzyMatchLevel.Balanced:
            default:
                if (wordLength >= 8) return 2;
                if (wordLength >= 5) return 1;
                return -1;
        }
    }
}
