using System.Text;
using GobchatEx.Core;
using static GobchatEx.Core.Tests.SpanTestHelpers;

namespace GobchatEx.Core.Tests;

public sealed class MentionMatcherTests
{
    private static (string Text, SegmentType Type)[] Find(string text, params string[] triggers)
        => Render(text, new MentionMatcher(triggers).FindMentions(text));

    private static (string Text, SegmentType Type)[] FindRules(string text, MentionRules rules)
        => Render(text, new MentionMatcher(rules).FindMentions(text));

    // Map ASCII letters to their Mathematical Sans-Serif Bold code points (each a surrogate pair) —
    // the headline real-world "fancy font" case ("𝗙𝗟𝗨𝗫" instead of "FLUX").
    private static string ToMathBold(string ascii)
    {
        var sb = new StringBuilder();
        foreach (var c in ascii)
        {
            if (c >= 'A' && c <= 'Z')
                sb.Append(char.ConvertFromUtf32(0x1D5D4 + (c - 'A')));
            else if (c >= 'a' && c <= 'z')
                sb.Append(char.ConvertFromUtf32(0x1D5EE + (c - 'a')));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    [Fact]
    public void CaseInsensitive_WholeWordMatch()
    {
        Find("hey Ali, over here", "ali").Should().Equal(
            ("Ali", SegmentType.Mention));
    }

    [Fact]
    public void InsideWord_DoesNotMatch()
    {
        Find("Alice and Kalim ignored it", "Ali").Should().BeEmpty();
    }

    [Fact]
    public void OverlappingTriggers_MergeToUnion()
    {
        // Full name and last name overlap on the same text; trigger order
        // must not matter (the CHT-1 regression from the old suite).
        Find("Alice Waved hello", "Alice Waved", "waved").Should().Equal(
            ("Alice Waved", SegmentType.Mention));
        Find("Alice Waved hello", "waved", "Alice Waved").Should().Equal(
            ("Alice Waved", SegmentType.Mention));
    }

    [Fact]
    public void TouchingMatches_MergeToOneInterval()
    {
        Find("a--b", "a-", "-b").Should().Equal(
            ("a--b", SegmentType.Mention));
    }

    [Fact]
    public void ApostropheName_Matches()
    {
        Find("R'ashaht Rhiki salutes", "R'ashaht").Should().Equal(
            ("R'ashaht", SegmentType.Mention));
    }

    [Fact]
    public void TriggerEndingInPunctuation_BoundsCorrectly()
    {
        Find("hey Ali. said hi", "Ali.").Should().Equal(
            ("Ali.", SegmentType.Mention));
        Find("hey Ali.x said hi", "Ali.").Should().BeEmpty();
    }

    [Fact]
    public void MultipleDisjointMatches_AllFound()
    {
        Find("bob met bob", "bob").Should().Equal(
            ("bob", SegmentType.Mention),
            ("bob", SegmentType.Mention));
    }

    [Fact]
    public void EmptyAndWhitespaceTriggers_AreFiltered()
    {
        var matcher = new MentionMatcher(["", "   "]);
        matcher.HasTriggers.Should().BeFalse();
        matcher.FindMentions("anything at all").Should().BeEmpty();
    }

    [Fact]
    public void NoTriggers_MatcherIsInert()
    {
        var matcher = new MentionMatcher([]);
        matcher.HasTriggers.Should().BeFalse();
        matcher.FindMentions("anything").Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // NFKC folding (fancy-font text)
    // ------------------------------------------------------------------

    [Fact]
    public void WholeWord_MathBoldText_MatchesAsciiTriggerAndHighlightsOriginalSpan()
    {
        var mathBoldAlice = ToMathBold("Alice");
        var text = $"hey {mathBoldAlice} over here";

        Find(text, "alice").Should().Equal(
            (mathBoldAlice, SegmentType.Mention));
    }

    // ------------------------------------------------------------------
    // Partial (substring) matching
    // ------------------------------------------------------------------

    [Fact]
    public void PartialWord_MatchesInsideWord()
    {
        var rules = new MentionRules(WholeWords: [], PartialWords: ["Sam"], FuzzyWords: [], FuzzyMatchLevel.Conservative);

        FindRules("Samantha waves", rules).Should().Equal(
            ("Sam", SegmentType.Mention));
    }

    [Fact]
    public void PartialWord_CaseInsensitive()
    {
        var rules = new MentionRules(WholeWords: [], PartialWords: ["sam"], FuzzyWords: [], FuzzyMatchLevel.Conservative);

        FindRules("SAMANTHA waves", rules).Should().Equal(
            ("SAM", SegmentType.Mention));
    }

    // ------------------------------------------------------------------
    // Fuzzy (typo-tolerant) matching
    // ------------------------------------------------------------------

    [Fact]
    public void FuzzyWord_SingleSubstitutionTypo_Matches()
    {
        var rules = new MentionRules(WholeWords: [], PartialWords: [], FuzzyWords: ["Elara"], FuzzyMatchLevel.Conservative);

        FindRules("hey Elora there", rules).Should().Equal(
            ("Elora", SegmentType.Mention));
    }

    [Fact]
    public void FuzzyWord_AdjacentTransposition_Matches()
    {
        var rules = new MentionRules(WholeWords: [], PartialWords: [], FuzzyWords: ["Khit'to"], FuzzyMatchLevel.Conservative);

        FindRules("hi Kiht'to there", rules).Should().Equal(
            ("Kiht'to", SegmentType.Mention));
    }

    [Fact]
    public void FuzzyWord_ShortWord_NeverFuzzyMatches()
    {
        // "Ana" is only 3 letters; Conservative requires length >= 5 to fuzzy-match at all, so nearby
        // words ("any") must never light up as a typo of it.
        var rules = new MentionRules(WholeWords: [], PartialWords: [], FuzzyWords: ["Ana"], FuzzyMatchLevel.Conservative);

        FindRules("at any point", rules).Should().BeEmpty();
    }

    [Fact]
    public void FuzzyWord_ExactMatch_AlsoMatchesViaFuzzyPass()
    {
        var rules = new MentionRules(WholeWords: [], PartialWords: [], FuzzyWords: ["Elara"], FuzzyMatchLevel.Conservative);

        FindRules("hey Elara there", rules).Should().Equal(
            ("Elara", SegmentType.Mention));
    }

    [Fact]
    public void FuzzyAndWholeWord_OverlappingMatch_MergesToOneSpan()
    {
        // The same word configured both as an exact trigger and a fuzzy candidate must not produce two
        // overlapping mention spans for one occurrence.
        var rules = new MentionRules(WholeWords: ["Elara"], PartialWords: [], FuzzyWords: ["Elara"], FuzzyMatchLevel.Conservative);

        FindRules("hey Elara there", rules).Should().Equal(
            ("Elara", SegmentType.Mention));
    }

    [Fact]
    public void MentionRules_AllEmpty_HasTriggersIsFalse()
    {
        var matcher = new MentionMatcher(new MentionRules([], [], [], FuzzyMatchLevel.Conservative));
        matcher.HasTriggers.Should().BeFalse();
        matcher.FindMentions("anything").Should().BeEmpty();
    }
}
