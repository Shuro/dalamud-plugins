using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// Player mentions turn the logged-in character's name into name-derived trigger words.
/// WHY this matters: a FFXIV first name can be a common English word ("Sun", "Bell"), so each name
/// part must be independently toggleable — always matching every part would mis-highlight ordinary RP.
/// The resolver also feeds the same whole-word finder, so it must hand over clean, de-duplicated words.
/// Custom-word merging and fuzzy-candidate composition now live in <see cref="MentionRuleBuilder"/>
/// (see MentionRuleBuilderTests) — this resolver only turns name parts into words.
/// </summary>
public sealed class PlayerMentionResolverTests
{
    // Convenience wrapper: the three "partial / Miqo'te" switches default off so the existing
    // whole-word cases stay readable; the dedicated tests below opt them in explicitly.
    private static PlayerMentionWords Resolve(
        string? fullName,
        bool matchFullName,
        bool matchFirstName,
        bool matchLastName,
        bool partialFirst = false,
        bool partialLast = false,
        bool miqote = false)
    {
        return PlayerMentionResolver.ResolveWords(
            fullName!, matchFullName, matchFirstName, matchLastName,
            partialFirst, partialLast, miqote);
    }

    [Fact]
    public void ResolveWords_AllParts_ReturnsFullFirstAndLast()
    {
        var words = Resolve("Max Mustermiqo'te", true, true, true);

        words.WholeWords.Should().Equal("Max Mustermiqo'te", "Max", "Mustermiqo'te");
        words.PartialWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_OnlyLastName_OmitsFullAndFirst()
    {
        // A user who only wants the surname highlighted (e.g. first name is a common word) must not
        // get the full name or forename smuggled back in.
        var words = Resolve("Sun Seeker", false, false, true);

        words.WholeWords.Should().Equal("Seeker");
    }

    [Fact]
    public void ResolveWords_SingleTokenName_DoesNotDuplicate()
    {
        // A one-word name means full == first == last; it must collapse to a single trigger word.
        var words = Resolve("Cloud", true, true, true);

        words.WholeWords.Should().Equal("Cloud");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveWords_BlankName_IsEmpty(string? name)
    {
        var words = Resolve(name, true, true, true);

        words.WholeWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_NoPartsSelected_IsEmpty()
    {
        var words = Resolve("Max Mustermiqo'te", false, false, false);

        words.WholeWords.Should().BeEmpty();
        words.PartialWords.Should().BeEmpty();
    }

    // --- Partial matching --------------------------------------------------------------------

    [Fact]
    public void ResolveWords_PartialFirstName_GoesToPartialNotWhole()
    {
        // With the first-name part enabled and refined to partial, the forename must be matched as
        // a substring, so it belongs in the partial list — and must NOT also sit in the whole-word
        // list (that would be redundant and risk double-marking).
        var words = Resolve("John Doe", false, true, false, partialFirst: true);

        words.PartialWords.Should().Equal("John");
        words.WholeWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_PartialLastName_GoesToPartial()
    {
        var words = Resolve("Some Gobchat", false, false, true, partialLast: true);

        words.PartialWords.Should().Equal("Gobchat");
        words.WholeWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_PartialFirst_WithoutFirstName_AddsNothing()
    {
        // A partial switch is a refinement of its name part, not an independent trigger: the
        // settings UI disables "Partial first name" while "First name" is off, so a stale partial
        // flag without its base part must not match — a greyed-out switch that still fires would
        // contradict what the UI shows.
        var words = Resolve("John Doe", false, false, false, partialFirst: true);

        words.PartialWords.Should().BeEmpty();
        words.WholeWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_PartialLast_WithoutLastName_AddsNothing()
    {
        var words = Resolve("Some Gobchat", false, false, false, partialLast: true);

        words.PartialWords.Should().BeEmpty();
        words.WholeWords.Should().BeEmpty();
    }

    // --- Miqo'te mode ------------------------------------------------------------------------

    [Theory]
    [InlineData("A'nabelle Surana", "nabelle")] // tribe prefix is the short part -> keep the longer tail
    [InlineData("Kiht'to Surana", "Kiht")]      // the longer part is before the apostrophe
    [InlineData("Y'shtola Rhul", "shtola")]
    public void ResolveWords_Miqote_AddsLongestApostropheSegment(string name, string expected)
    {
        var words = Resolve(name, false, false, false, miqote: true);

        words.WholeWords.Should().Contain(expected);
    }

    [Fact]
    public void ResolveWords_Miqote_NoApostrophe_AddsNothing()
    {
        // The forename has no apostrophe, so Miqo'te mode must contribute no extra word.
        var words = Resolve("John Doe", false, false, false, miqote: true);

        words.WholeWords.Should().BeEmpty();
        words.PartialWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_Miqote_EqualLengthSegments_FirstOneWins()
    {
        // Tie-break pinned: with equal-length apostrophe segments the FIRST one is kept
        // ("strictly longer" replacement) — the derived short name must be deterministic,
        // not flip with implementation details of the scan.
        var words = Resolve("Mira'lena Doe", false, false, false, miqote: true);

        words.WholeWords.Should().Equal("Mira");
    }

    [Fact]
    public void ResolveWords_Miqote_AlongsideFirstName_KeepsBoth()
    {
        // Matching the whole forename and the Miqo'te short name are independent; both whole words show up.
        var words = Resolve("A'nabelle Surana", false, true, false, miqote: true);

        words.WholeWords.Should().Equal("A'nabelle", "nabelle");
    }
}
