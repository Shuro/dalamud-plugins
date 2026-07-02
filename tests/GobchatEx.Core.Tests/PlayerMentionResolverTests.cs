using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// Player mentions turn the logged-in character's name (and optional extra words) into trigger words.
/// WHY this matters: a FFXIV first name can be a common English word ("Sun", "Bell"), so each name
/// part must be independently toggleable — always matching every part would mis-highlight ordinary RP.
/// The resolver also feeds the same whole-word finder, so it must hand over clean, de-duplicated words.
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
        IEnumerable<string>? custom,
        bool partialFirst = false,
        bool partialLast = false,
        bool miqote = false)
    {
        return PlayerMentionResolver.ResolveWords(
            fullName!, matchFullName, matchFirstName, matchLastName,
            partialFirst, partialLast, miqote, custom);
    }

    [Fact]
    public void ResolveWords_AllParts_ReturnsFullFirstAndLast()
    {
        var words = Resolve("Max Mustermiqo'te", true, true, true, null);

        words.WholeWords.Should().Equal("Max Mustermiqo'te", "Max", "Mustermiqo'te");
        words.PartialWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_OnlyLastName_OmitsFullAndFirst()
    {
        // A user who only wants the surname highlighted (e.g. first name is a common word) must not
        // get the full name or forename smuggled back in.
        var words = Resolve("Sun Seeker", false, false, true, null);

        words.WholeWords.Should().Equal("Seeker");
    }

    [Fact]
    public void ResolveWords_MergesCustomWords()
    {
        var words = Resolve("Max Mustermiqo'te", false, true, false, ["boss", "captain"]);

        words.WholeWords.Should().Equal("Max", "boss", "captain");
    }

    [Fact]
    public void ResolveWords_DeduplicatesCaseInsensitively_KeepingFirstCasing()
    {
        // The custom list repeating a name part (in another case) must not produce a duplicate regex;
        // the finder already matches case-insensitively, so duplicates are pure noise.
        var words = Resolve("Max Mustermiqo'te", false, true, false, ["MAX", "  max  "]);

        words.WholeWords.Should().Equal("Max");
    }

    [Fact]
    public void ResolveWords_SingleTokenName_DoesNotDuplicate()
    {
        // A one-word name means full == first == last; it must collapse to a single trigger word.
        var words = Resolve("Cloud", true, true, true, null);

        words.WholeWords.Should().Equal("Cloud");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveWords_BlankName_YieldsOnlyCustomWords(string? name)
    {
        var words = Resolve(name, true, true, true, ["ally"]);

        words.WholeWords.Should().Equal("ally");
    }

    [Fact]
    public void ResolveWords_NoPartsAndNoCustom_IsEmpty()
    {
        var words = Resolve("Max Mustermiqo'te", false, false, false, []);

        words.WholeWords.Should().BeEmpty();
        words.PartialWords.Should().BeEmpty();
    }

    // --- Partial matching --------------------------------------------------------------------

    [Fact]
    public void ResolveWords_PartialFirstName_GoesToPartialNotWhole()
    {
        // Partial first name must be matched as a substring, so it belongs in the partial list — and
        // must NOT also sit in the whole-word list (that would be redundant and risk double-marking).
        var words = Resolve("John Doe", false, false, false, null, partialFirst: true);

        words.PartialWords.Should().Equal("John");
        words.WholeWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_PartialLastName_GoesToPartial()
    {
        var words = Resolve("Some Gobchat", false, false, false, null, partialLast: true);

        words.PartialWords.Should().Equal("Gobchat");
        words.WholeWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_PartialFirst_WinsOverWholeFirst()
    {
        // With both the whole and partial first-name switches on, the forename must resolve to the
        // partial list only — a substring match already covers the whole word.
        var words = Resolve("John Doe", false, true, false, null, partialFirst: true);

        words.PartialWords.Should().Equal("John");
        words.WholeWords.Should().NotContain("John");
    }

    // --- Miqo'te mode ------------------------------------------------------------------------

    [Theory]
    [InlineData("A'nabelle Surana", "nabelle")] // tribe prefix is the short part -> keep the longer tail
    [InlineData("Kiht'to Surana", "Kiht")]      // the longer part is before the apostrophe
    [InlineData("Y'shtola Rhul", "shtola")]
    public void ResolveWords_Miqote_AddsLongestApostropheSegment(string name, string expected)
    {
        var words = Resolve(name, false, false, false, null, miqote: true);

        words.WholeWords.Should().Contain(expected);
    }

    [Fact]
    public void ResolveWords_Miqote_NoApostrophe_AddsNothing()
    {
        // The forename has no apostrophe, so Miqo'te mode must contribute no extra word.
        var words = Resolve("John Doe", false, false, false, null, miqote: true);

        words.WholeWords.Should().BeEmpty();
        words.PartialWords.Should().BeEmpty();
    }

    [Fact]
    public void ResolveWords_Miqote_AlongsideFirstName_KeepsBoth()
    {
        // Matching the whole forename and the Miqo'te short name are independent; both whole words show up.
        var words = Resolve("A'nabelle Surana", false, true, false, null, miqote: true);

        words.WholeWords.Should().Equal("A'nabelle", "nabelle");
    }

    // --- Fuzzy candidates --------------------------------------------------------------------

    [Fact]
    public void FuzzyCandidates_IncludePartialNamesAsWholeWords()
    {
        // Regression guard: turning on a partial switch must NOT drop that name from fuzzy. The
        // partially-matched forename is still a fuzzy candidate, alongside the whole-word surname.
        var words = Resolve("John Doe", false, false, true, null, partialFirst: true);

        var fuzzy = PlayerMentionResolver.FuzzyCandidates(words);

        fuzzy.Should().Contain("John");
        fuzzy.Should().Contain("Doe");
    }

    [Fact]
    public void FuzzyCandidates_DeduplicateAcrossLists()
    {
        // A single-token name landing in both lists (full name whole + partial first) must yield one
        // fuzzy candidate, not a duplicate.
        var words = Resolve("Cloud", true, false, false, null, partialFirst: true);

        PlayerMentionResolver.FuzzyCandidates(words).Should().Equal("Cloud");
    }

    [Fact]
    public void FuzzyCandidates_NoWords_IsEmpty()
    {
        var words = Resolve("John Doe", false, false, false, null);

        PlayerMentionResolver.FuzzyCandidates(words).Should().BeEmpty();
    }
}
