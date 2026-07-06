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
    public List<string> CustomWords { get; set; } = [];

    public CharacterMentionSettings Clone() => new()
    {
        Name = Name,
        Active = Active,
        MatchFullName = MatchFullName,
        MatchFirstName = MatchFirstName,
        MatchLastName = MatchLastName,
        MatchFirstNamePartial = MatchFirstNamePartial,
        MatchLastNamePartial = MatchLastNamePartial,
        MatchMiqote = MatchMiqote,
        MatchFuzzy = MatchFuzzy,
        FuzzyLevel = FuzzyLevel,
        CustomWords = [.. CustomWords],
    };
}
