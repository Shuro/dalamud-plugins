using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// <see cref="MentionRuleBuilder"/> assembles global triggers and one character's resolved words
/// into <see cref="MentionRules"/>, allocating a style id per distinct color override. WHY this
/// matters: the id allocation order is what makes <see cref="MentionMatcher"/>'s exact-tie merge
/// priority deterministic (config order, not hash/regex order) — and the first-wins dedupe here is
/// the only place "which source's color wins a duplicate word" is decided.
/// </summary>
public sealed class MentionRuleBuilderTests
{
    private static StyledTrigger Unstyled(string word) => new(word, 0, 0);

    private static CharacterMentionInput Character(
        string name = "Max Mustermann",
        bool matchFullName = false,
        bool matchFirstName = false,
        bool matchLastName = false,
        bool matchFirstNamePartial = false,
        bool matchLastNamePartial = false,
        bool matchMiqote = false,
        bool matchFuzzy = false,
        uint nameForeground = 0,
        uint nameGlow = 0,
        IReadOnlyList<StyledTrigger>? customWords = null)
        => new(name, matchFullName, matchFirstName, matchLastName, matchFirstNamePartial,
            matchLastNamePartial, matchMiqote, matchFuzzy, FuzzyMatchLevel.Conservative,
            nameForeground, nameGlow, customWords ?? []);

    [Fact]
    public void AllUnstyled_StylesTableEmpty_EveryWordGetsIdZero()
    {
        // The no-behavior-change contract: a config with no colors configured must produce byte-
        // identical matching to the pre-styling matcher (every word id 0, empty style table).
        var rules = MentionRuleBuilder.Build(
            [Unstyled("alpha"), Unstyled("beta")],
            Character(matchFullName: true, name: "Gamma Delta"));

        rules.Styles.Should().BeEmpty();
        rules.WholeWords.Should().OnlyContain(w => w.StyleId == 0);
    }

    [Fact]
    public void GlobalTriggers_AllocateDistinctStyleIds_InConfigOrder()
    {
        var rules = MentionRuleBuilder.Build(
            [new StyledTrigger("alpha", 0xFF0000FF, 0), new StyledTrigger("beta", 0x00FF00FF, 0)],
            character: null);

        rules.Styles.Should().Equal((0xFF0000FFu, 0u), (0x00FF00FFu, 0u));
        rules.WholeWords.Should().Equal(
            new MentionWord("alpha", 1), new MentionWord("beta", 2));
    }

    [Fact]
    public void IdenticalColorPairs_ShareOneStyleId()
    {
        // Two words configured with the exact same override must intern to one style entry — so a
        // touch-merge between them still coalesces into a single span (same-style rule).
        var rules = MentionRuleBuilder.Build(
            [new StyledTrigger("alpha", 0xFF0000FF, 0), new StyledTrigger("beta", 0xFF0000FF, 0)],
            character: null);

        rules.Styles.Should().ContainSingle();
        rules.WholeWords.Should().OnlyContain(w => w.StyleId == 1);
    }

    [Fact]
    public void Character_AllNameDerivedWords_ShareOneStyleId()
    {
        // Full/first/last/partial/Miqo'te/fuzzy are all "this character's name" — one color for the
        // whole character, not a different one per derived word.
        var rules = MentionRuleBuilder.Build(
            [],
            Character(name: "Aya Rhiki", matchFullName: true, matchFirstName: true, matchLastName: true,
                nameForeground: 0xAABBCCFF, nameGlow: 0x11223344));

        rules.Styles.Should().Equal((0xAABBCCFFu, 0x11223344u));
        rules.WholeWords.Should().OnlyContain(w => w.StyleId == 1);
    }

    [Fact]
    public void CustomWordDuplicatingNamePart_IsDropped_NameStyleWins()
    {
        // First-wins order: global > character name > custom word. A custom word that repeats a
        // name part must not resurrect it with a different (custom) color.
        var rules = MentionRuleBuilder.Build(
            [],
            Character(name: "Max Mustermann", matchFirstName: true, nameForeground: 0x111111FF,
                customWords: [new StyledTrigger("Max", 0x222222FF, 0)]));

        rules.WholeWords.Should().ContainSingle(w => w.Word == "Max" && w.StyleId != 0);
        rules.Styles.Should().Equal((0x111111FFu, 0u));
    }

    [Fact]
    public void CustomWordDuplicatingGlobalTrigger_IsDropped_GlobalStyleWins()
    {
        var rules = MentionRuleBuilder.Build(
            [new StyledTrigger("boss", 0x111111FF, 0)],
            Character(customWords: [new StyledTrigger("BOSS", 0x222222FF, 0)]));

        rules.WholeWords.Should().ContainSingle();
        rules.Styles.Should().Equal((0x111111FFu, 0u));
    }

    [Fact]
    public void CustomWords_GetTheirOwnStyleIds()
    {
        var rules = MentionRuleBuilder.Build(
            [],
            Character(customWords: [new StyledTrigger("boss", 0x333333FF, 0), Unstyled("captain")]));

        rules.WholeWords.Should().Equal(
            new MentionWord("boss", 1), new MentionWord("captain", 0));
        rules.Styles.Should().Equal((0x333333FFu, 0u));
    }

    [Fact]
    public void MatchFuzzyOff_FuzzyWordsEmpty()
    {
        var rules = MentionRuleBuilder.Build(
            [],
            Character(name: "John Doe", matchFullName: true, matchFuzzy: false));

        rules.FuzzyWords.Should().BeEmpty();
    }

    [Fact]
    public void FuzzyCandidates_ComposeNameWholePartialAndCustom_ExcludingGlobals()
    {
        // Regression guard ported from the old PlayerMentionResolver.FuzzyCandidates tests: turning
        // on a partial switch must not drop that name from fuzzy, and custom words join the fuzzy
        // set too — but global triggers never do. (Partial now requires its base part enabled,
        // hence matchFirstName on.)
        var rules = MentionRuleBuilder.Build(
            [Unstyled("globalword")],
            Character(name: "John Doe", matchFirstName: true, matchLastName: true,
                matchFirstNamePartial: true, matchFuzzy: true, customWords: [Unstyled("ally")]));

        rules.FuzzyWords.Select(w => w.Word).Should().BeEquivalentTo("John", "Doe", "ally");
    }

    [Fact]
    public void FuzzyCandidates_DeduplicateAcrossNameAndPartial()
    {
        // A single-token name landing in both the whole and partial lists (full name + partial
        // first) must yield one fuzzy candidate, not a duplicate.
        var rules = MentionRuleBuilder.Build(
            [],
            Character(name: "Cloud", matchFullName: true, matchFirstName: true,
                matchFirstNamePartial: true, matchFuzzy: true));

        rules.FuzzyWords.Select(w => w.Word).Should().Equal("Cloud");
    }

    [Fact]
    public void FuzzyWords_UseCharacterStyleId()
    {
        var rules = MentionRuleBuilder.Build(
            [],
            Character(name: "John Doe", matchFullName: true, matchFuzzy: true, nameForeground: 0xABCDEFFF));

        rules.FuzzyWords.Should().OnlyContain(w => w.StyleId == 1);
    }

    [Fact]
    public void NoCharacter_OnlyGlobalWholeWordsProduced()
    {
        var rules = MentionRuleBuilder.Build([Unstyled("alpha")], character: null);

        rules.WholeWords.Should().Equal(new MentionWord("alpha", 0));
        rules.PartialWords.Should().BeEmpty();
        rules.FuzzyWords.Should().BeEmpty();
    }
}
