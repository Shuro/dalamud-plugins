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

namespace GobchatEx.Core;

/// <summary>
/// The words a logged-in character's name and per-character settings resolve to, split by how the
/// finder should match them: <see cref="WholeWords"/> are matched whole-word (the default) and
/// <see cref="PartialWords"/> as substrings (the opt-in "partial first/last name" switches).
/// </summary>
public sealed record PlayerMentionWords(
    IReadOnlyList<string> WholeWords,
    IReadOnlyList<string> PartialWords);

/// <summary>
/// Turns a logged-in character's name and per-character settings into the words that should be
/// treated as mentions. FFXIV names are always "Forename Surname", so the forename is the first
/// whitespace-separated token and the surname the last.
///
/// Whole words are later matched whole-word and case-insensitively; partial words as case-insensitive
/// substrings. Words are returned trimmed and de-duplicated (case-insensitively) within each list,
/// preserving the first occurrence's casing.
///
/// A partial flag wins over the matching whole flag for that name part: with "partial first name"
/// on, the forename goes to <see cref="PlayerMentionWords.PartialWords"/> only (a substring match
/// already covers the whole word), so the two lists stay disjoint. "Miqo'te mode" adds, for an
/// apostrophe forename, the longest apostrophe-split segment as a whole word
/// (e.g. <c>A'nabelle</c> → <c>nabelle</c>, <c>Kiht'to</c> → <c>Kiht</c>).
/// </summary>
public static class PlayerMentionResolver
{
    public static PlayerMentionWords ResolveWords(
        string fullName,
        bool matchFullName,
        bool matchFirstName,
        bool matchLastName,
        bool matchFirstNamePartial,
        bool matchLastNamePartial,
        bool matchMiqote,
        IEnumerable<string>? customMentions)
    {
        var whole = new List<string>();
        var partial = new List<string>();

        static void Add(List<string> target, string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;
            var trimmed = word.Trim();
            if (trimmed.Length == 0)
                return;
            if (!target.Any(w => string.Equals(w, trimmed, StringComparison.OrdinalIgnoreCase)))
                target.Add(trimmed);
        }

        var name = fullName?.Trim() ?? string.Empty;
        if (name.Length > 0)
        {
            var parts = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (matchFullName)
                Add(whole, name);

            if (parts.Length > 0)
            {
                var first = parts[0];
                if (matchFirstNamePartial)
                    Add(partial, first);
                else if (matchFirstName)
                    Add(whole, first);

                var last = parts[parts.Length - 1];
                if (matchLastNamePartial)
                    Add(partial, last);
                else if (matchLastName)
                    Add(whole, last);

                if (matchMiqote)
                {
                    var derived = LongestApostropheSegment(first);
                    if (derived != null)
                        Add(whole, derived);
                }
            }
        }

        if (customMentions != null)
            foreach (var custom in customMentions)
                Add(whole, custom);

        return new PlayerMentionWords(whole, partial);
    }

    /// <summary>
    /// The words eligible for fuzzy (typo) matching for a resolved character: every name the
    /// character wants matched, whole-word and partial alike (partial names are fuzzed as whole
    /// words), de-duplicated case-insensitively. Living here — not in the consuming module — keeps a
    /// partial switch from silently dropping that name out of fuzzy matching.
    /// </summary>
    public static IReadOnlyList<string> FuzzyCandidates(PlayerMentionWords words)
    {
        if (words == null)
            return Array.Empty<string>();
        return words.WholeWords
            .Concat(words.PartialWords)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// For an apostrophe forename (Seeker/Keeper Miqo'te names), the longest piece left after
    /// splitting on the apostrophe — the part actually used as a short name. Returns null when the
    /// name has no apostrophe (so nothing extra is matched). Splits on the straight apostrophe
    /// only, deliberately: FFXIV's naming rules allow only the straight form, so a curly variant
    /// can't occur in a character name — unlike free-typed message text, where
    /// MentionMatcher folds curly onto straight.
    /// </summary>
    private static string? LongestApostropheSegment(string firstName)
    {
        if (string.IsNullOrEmpty(firstName) || firstName.IndexOf('\'') < 0)
            return null;

        string? longest = null;
        foreach (var segment in firstName.Split('\''))
        {
            var piece = segment.Trim();
            if (piece.Length == 0)
                continue;
            if (longest == null || piece.Length > longest.Length)
                longest = piece;
        }
        return longest;
    }
}
