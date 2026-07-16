/*******************************************************************************
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

namespace GobchatEx.Core;

/// <summary>A global mention trigger word plus its optional per-word color override (0/0 = none).</summary>
public sealed record StyledTrigger(string Word, uint Foreground, uint Glow);

/// <summary>
/// A logged-in character's resolved mention settings, plain-value form (no Dalamud/Config
/// dependency): name-part match flags identical to <c>CharacterMentionSettings</c>, one shared
/// color override for every name-derived word (<paramref name="NameForeground"/>/
/// <paramref name="NameGlow"/> — a whole character, not each derived word, gets one style), and its
/// styled custom words.
/// </summary>
public sealed record CharacterMentionInput(
    string Name,
    bool MatchFullName,
    bool MatchFirstName,
    bool MatchLastName,
    bool MatchFirstNamePartial,
    bool MatchLastNamePartial,
    bool MatchMiqote,
    bool MatchFuzzy,
    FuzzyMatchLevel FuzzyLevel,
    uint NameForeground,
    uint NameGlow,
    IReadOnlyList<StyledTrigger> CustomWords);

/// <summary>
/// Assembles global trigger words and (optionally) one character's resolved name/custom words into
/// the <see cref="MentionRules"/> the matcher runs against, allocating a style id per distinct
/// per-word color override. Ids are allocated in a fixed, user-visible order — global triggers in
/// config order, then the character's shared name style, then its custom words in config order —
/// so <see cref="MentionMatcher"/>'s exact-tie merge priority (favors the lower id) resolves
/// deterministically and reproducibly across rebuilds. Pure and Config-free so it's directly
/// testable (ADR 0002); the Chat-layer caller maps <c>MentionsConfig</c>/<c>IPlayerState</c> into
/// these plain inputs.
/// </summary>
public static class MentionRuleBuilder
{
    public static MentionRules Build(IReadOnlyList<StyledTrigger> globalTriggers, CharacterMentionInput? character)
    {
        var styles = new List<(uint Foreground, uint Glow)>();
        var styleIds = new Dictionary<(uint Foreground, uint Glow), int>();

        int StyleIdFor(uint foreground, uint glow)
        {
            if (foreground == 0 && glow == 0)
                return 0;

            var key = (foreground, glow);
            if (styleIds.TryGetValue(key, out var existing))
                return existing;

            styles.Add(key);
            var id = styles.Count;
            styleIds[key] = id;
            return id;
        }

        var whole = new List<MentionWord>();
        var partial = new List<MentionWord>();
        var fuzzy = new List<MentionWord>();
        var fuzzyLevel = FuzzyMatchLevel.Conservative;

        // First-wins case-insensitive dedupe across whole words, in the same priority order as
        // the pre-styling ChatListener.BuildMentionRules: global triggers, then a character's
        // name-derived words, then its custom words — so a duplicate keeps the earlier source's
        // style too, not just its word. The style id is allocated only once the dedupe check
        // passes, so a word dropped as a duplicate never leaves an unused entry in the style table.
        var seenWhole = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddWhole(string word, uint foreground, uint glow)
        {
            var trimmed = word?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || !seenWhole.Add(trimmed))
                return;
            whole.Add(new MentionWord(trimmed, StyleIdFor(foreground, glow)));
        }

        foreach (var trigger in globalTriggers)
            AddWhole(trigger.Word, trigger.Foreground, trigger.Glow);

        if (character != null)
        {
            var resolved = PlayerMentionResolver.ResolveWords(
                character.Name,
                character.MatchFullName,
                character.MatchFirstName,
                character.MatchLastName,
                character.MatchFirstNamePartial,
                character.MatchLastNamePartial,
                character.MatchMiqote);

            foreach (var word in resolved.WholeWords)
                AddWhole(word, character.NameForeground, character.NameGlow);

            foreach (var word in resolved.PartialWords)
                partial.Add(new MentionWord(word, StyleIdFor(character.NameForeground, character.NameGlow)));

            foreach (var custom in character.CustomWords)
                AddWhole(custom.Word, custom.Foreground, custom.Glow);

            // Fuzzy candidates: every name the character wants matched (whole and partial alike —
            // partial names are fuzzed as whole words) plus its custom words, de-duplicated
            // case-insensitively. Global triggers are never fuzzed, as before per-word styling.
            if (character.MatchFuzzy)
            {
                fuzzyLevel = character.FuzzyLevel;
                var seenFuzzy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void AddFuzzy(string word, int styleId)
                {
                    var trimmed = word?.Trim() ?? string.Empty;
                    if (trimmed.Length == 0 || !seenFuzzy.Add(trimmed))
                        return;
                    fuzzy.Add(new MentionWord(trimmed, styleId));
                }

                foreach (var word in resolved.WholeWords)
                    AddFuzzy(word, StyleIdFor(character.NameForeground, character.NameGlow));
                foreach (var word in resolved.PartialWords)
                    AddFuzzy(word, StyleIdFor(character.NameForeground, character.NameGlow));
                foreach (var custom in character.CustomWords)
                    AddFuzzy(custom.Word, StyleIdFor(custom.Foreground, custom.Glow));
            }
        }

        return new MentionRules(whole, partial, fuzzy, fuzzyLevel, styles);
    }
}
