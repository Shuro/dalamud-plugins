using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// Backs fuzzy player-mention matching. WHY this matters: the distance must count an adjacent letter
/// swap as a single edit (so "Kiht'to" still reaches "Khit'to"), and the per-length budget is the only
/// guard between "catch the user's typos" and "highlight ordinary words" — both the transposition rule
/// and the tier thresholds are load-bearing, so they are pinned here.
/// </summary>
public sealed class StringSimilarityTests
{
    [Theory]
    [InlineData("Elara", "Elara", 0)]
    [InlineData("Elara", "Elora", 1)]    // substitution a->o
    [InlineData("Elara", "Ellara", 1)]   // insertion of l
    [InlineData("Elara", "Elarah", 1)]   // append
    [InlineData("Khit'to", "Khitto", 1)] // deletion of the apostrophe
    [InlineData("Khit'to", "Khit'o", 1)] // deletion of a t
    [InlineData("Khit'to", "Kiht'to", 1)] // transposition hi -> ih
    [InlineData("Elara", "Amara", 2)]    // two substitutions
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    public void OsaDistance_CountsEditsAsExpected(string a, string b, int expected)
    {
        StringSimilarity.OsaDistance(a, b).Should().Be(expected);
    }

    [Fact]
    public void OsaDistance_TreatsAdjacentSwapAsOneEdit()
    {
        // The whole reason OSA is used over plain Levenshtein (which would score this 2).
        StringSimilarity.OsaDistance("ab", "ba").Should().Be(1);
    }

    [Fact]
    public void OsaDistance_IsSymmetric()
    {
        StringSimilarity.OsaDistance("Khit'to", "Kiht'to")
            .Should().Be(StringSimilarity.OsaDistance("Kiht'to", "Khit'to"));
    }

    [Theory]
    // Conservative: exact-only below 5, then 1 edit at any length.
    [InlineData(FuzzyMatchLevel.Conservative, 4, -1)]
    [InlineData(FuzzyMatchLevel.Conservative, 5, 1)]
    [InlineData(FuzzyMatchLevel.Conservative, 12, 1)]
    // Balanced (default): exact-only below 5, 1 edit for 5-7, 2 edits at 8+.
    [InlineData(FuzzyMatchLevel.Balanced, 4, -1)]
    [InlineData(FuzzyMatchLevel.Balanced, 5, 1)]
    [InlineData(FuzzyMatchLevel.Balanced, 7, 1)]
    [InlineData(FuzzyMatchLevel.Balanced, 8, 2)]
    // Aggressive: exact-only below 4, 1 edit for 4-5, 2 edits at 6+.
    [InlineData(FuzzyMatchLevel.Aggressive, 3, -1)]
    [InlineData(FuzzyMatchLevel.Aggressive, 4, 1)]
    [InlineData(FuzzyMatchLevel.Aggressive, 5, 1)]
    [InlineData(FuzzyMatchLevel.Aggressive, 6, 2)]
    public void MaxDistanceFor_FollowsTierTable(FuzzyMatchLevel level, int wordLength, int expected)
    {
        StringSimilarity.MaxDistanceFor(level, wordLength).Should().Be(expected);
    }
}
