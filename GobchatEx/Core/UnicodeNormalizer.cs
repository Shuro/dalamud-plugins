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
using System.Text;

namespace GobchatEx.Core;

/// <summary>
/// Unicode NFKC normalization used for chat <em>matching only</em> (mentions, trigger groups) — the
/// displayed text always stays original. NFKC folds compatibility variants back to their plain form,
/// e.g. Mathematical Sans-Serif Bold "𝗙𝗟𝗨𝗫" (U+1D400–U+1D7FF) → "FLUX", so a rule written in plain
/// ASCII still matches a message typed in those decorative code points. NFKC does not remove
/// Format (Cf) code points such as zero-width spaces/joiners; matching treats them as ordinary
/// characters (see MentionMatcher's token regex for the accepted consequence).
/// </summary>
public static class UnicodeNormalizer
{
    /// <summary>
    /// NFKC-normalizes <paramref name="text"/> and returns a map translating an index in the
    /// normalized string back to the corresponding index in the original, so a match found on the
    /// normalized copy can be highlighted on the original substring.
    /// <para>
    /// <c>map[i]</c> is the original index of normalized char <c>i</c>; the array has length
    /// (normalized length + 1) with a final sentinel equal to the original length, so a match's end
    /// index maps cleanly. <c>Map</c> is <c>null</c> when the text is already normalized (identity
    /// mapping — the common all-ASCII case: no allocation, indices are used as-is).
    /// </para>
    /// </summary>
    public static (string Text, int[]? Map) NormalizeWithMap(string text)
    {
        if (string.IsNullOrEmpty(text) || IsNormalized(text))
            return (text, null);

        var builder = new StringBuilder(text.Length);
        var map = new List<int>(text.Length + 1);

        var index = 0;
        while (index < text.Length)
        {
            // Step a whole code point so surrogate pairs (math-bold lives above U+FFFF) normalize as
            // a unit. Per-code-point keeps the index map simple and is exact for the math-bold case
            // (each such code point folds to a single ASCII char).
            var unitLength = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            var unit = text.Substring(index, unitLength);

            string normalizedUnit;
            try
            {
                normalizedUnit = unit.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                normalizedUnit = unit; // invalid/unpaired surrogate from raw memory: keep as-is
            }

            foreach (var c in normalizedUnit)
            {
                builder.Append(c);
                map.Add(index);
            }

            index += unitLength;
        }

        map.Add(text.Length); // sentinel: a match End == normalized length maps to the original length
        return (builder.ToString(), map.ToArray());
    }

    /// <summary>
    /// NFKC-normalizes <paramref name="text"/> for plain comparison (no index map). Returns the input
    /// unchanged when it is already normalized or cannot be normalized (e.g. unpaired surrogates).
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        try
        {
            return text.IsNormalized(NormalizationForm.FormKC) ? text : text.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static bool IsNormalized(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return true; // can't be normalized -> treat as identity (use original indices)
        }
    }
}
