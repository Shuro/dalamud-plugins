using System;
using System.Linq;
using GobchatEx.Config;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Maps <see cref="MentionsConfig"/> onto <see cref="MentionRuleBuilder"/>'s plain inputs: the
/// global trigger words plus the logged-in character's resolved mention words (if that character
/// is remembered and active). The builder owns the union/dedupe/style-id logic (kept Config-free
/// so it's testable). Shared by the native chat pass, the Chat 2 provider's mention bypass and
/// the Mentions tab's tester; reads IPlayerState, so callers must be on the framework thread.
/// </summary>
internal static class MentionRulesFactory
{
    private static readonly MentionRules NoMentionRules = new([], [], [], FuzzyMatchLevel.Conservative);

    public static MentionRules Build(MentionsConfig config)
    {
        if (!config.MentionsEnabled)
            return NoMentionRules;

        var globalTriggers = config.MentionTriggers
            .Select(t => new StyledTrigger(t.Word, t.Foreground, t.Glow))
            .ToList();

        return MentionRuleBuilder.Build(globalTriggers, ActiveCharacter(config));
    }

    private static CharacterMentionInput? ActiveCharacter(MentionsConfig config)
    {
        if (!config.PlayerMentionsEnabled || !Plugin.PlayerState.IsLoaded)
            return null;

        var playerName = Plugin.PlayerState.CharacterName;
        var match = config.Characters.FirstOrDefault(c =>
            c.Active && string.Equals(c.Name, playerName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return null;

        return new CharacterMentionInput(
            match.Name,
            match.MatchFullName,
            match.MatchFirstName,
            match.MatchLastName,
            match.MatchFirstNamePartial,
            match.MatchLastNamePartial,
            match.MatchMiqote,
            match.MatchFuzzy,
            match.FuzzyLevel,
            match.NameForeground,
            match.NameGlow,
            match.CustomWords.Select(w => new StyledTrigger(w.Word, w.Foreground, w.Glow)).ToList());
    }
}
