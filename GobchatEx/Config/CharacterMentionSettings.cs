using System;
using System.Collections.Generic;
using GobchatEx.Core;

namespace GobchatEx.Config;

/// <summary>
/// Per-character player-mention settings: which name parts count as mentions
/// (whole-word and/or partial-substring), Miqo'te apostrophe-segment
/// matching, typo-tolerant fuzzy matching, and custom extra words. One
/// entry per character remembered via login (Milestone 1); mirrors the
/// standalone app's per-character mention template.
/// </summary>
[Serializable]
public class CharacterMentionSettings
{
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool MatchFullName { get; set; } = true;
    public bool MatchFirstName { get; set; } = true;
    public bool MatchLastName { get; set; } = true;
    public bool MatchFirstNamePartial { get; set; }
    public bool MatchLastNamePartial { get; set; }
    public bool MatchMiqote { get; set; }
    public bool MatchFuzzy { get; set; }
    public FuzzyMatchLevel FuzzyLevel { get; set; } = FuzzyMatchLevel.Conservative;

    /// <summary>Color/glow override applied to every name-derived match for this character (full
    /// name, first/last name, partial, Miqo'te, fuzzy alike) — one shared style per character, not
    /// per derived word. 0 = fall back to the default mention style.</summary>
    public uint NameForeground { get; set; }
    public uint NameGlow { get; set; }

    /// <summary>Extra words, each with an optional per-word color/glow override.</summary>
    public List<MentionTrigger> CustomWords { get; set; } = [];
}
