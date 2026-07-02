using GobchatEx.Core;
using static GobchatEx.Core.Tests.SpanTestHelpers;

namespace GobchatEx.Core.Tests;

public sealed class SegmentParserTests
{
    private static readonly TokenRule SayQuotes = new(SegmentType.Say, ["\""], ["\""]);
    private static readonly TokenRule Ooc = new(SegmentType.Ooc, ["(("], ["))"]);
    private static readonly TokenRule EmoteStar = new(SegmentType.Emote, ["*"], ["*"]);

    private static (string Text, SegmentType Type)[] ParseSingleRun(TokenRule rule, string text)
    {
        var result = Parse(rule, text);
        result.Should().HaveCount(1);
        AssertFullCoverage(text, result[0]);
        return Render(text, result[0]);
    }

    // ------------------------------------------------------------------
    // Single-run cases
    // ------------------------------------------------------------------

    [Fact]
    public void SimplePair_MarksQuoteIncludingDelimiters()
    {
        ParseSingleRun(SayQuotes, "he said \"hello\" there").Should().Equal(
            ("he said ", SegmentType.Undefined),
            ("\"hello\"", SegmentType.Say),
            (" there", SegmentType.Undefined));
    }

    [Fact]
    public void NoMatch_LeavesSingleUndefinedSpan()
    {
        ParseSingleRun(SayQuotes, "plain text without quotes").Should().Equal(
            ("plain text without quotes", SegmentType.Undefined));
    }

    [Fact]
    public void UnclosedDelimiter_MarksToEndOfRun()
    {
        ParseSingleRun(SayQuotes, "he said \"hello").Should().Equal(
            ("he said ", SegmentType.Undefined),
            ("\"hello", SegmentType.Say));
    }

    [Fact]
    public void EmptyPair_IsMarked()
    {
        ParseSingleRun(SayQuotes, "\"\"").Should().Equal(
            ("\"\"", SegmentType.Say));
    }

    [Fact]
    public void AdjacentPairs_ProduceAdjacentTypedSpans()
    {
        ParseSingleRun(SayQuotes, "\"a\"\"b\"").Should().Equal(
            ("\"a\"", SegmentType.Say),
            ("\"b\"", SegmentType.Say));
    }

    [Fact]
    public void MultiplePairs_AlternateWithUndefined()
    {
        ParseSingleRun(SayQuotes, "\"a\" and \"b\"").Should().Equal(
            ("\"a\"", SegmentType.Say),
            (" and ", SegmentType.Undefined),
            ("\"b\"", SegmentType.Say));
    }

    [Fact]
    public void MultiCharToken_DoesNotHalfMatchSingleChar()
    {
        ParseSingleRun(Ooc, "(smile) parens").Should().Equal(
            ("(smile) parens", SegmentType.Undefined));
    }

    [Fact]
    public void OocPair_MarksParenthesizedText()
    {
        ParseSingleRun(Ooc, "before ((brb)) after").Should().Equal(
            ("before ", SegmentType.Undefined),
            ("((brb))", SegmentType.Ooc),
            (" after", SegmentType.Undefined));
    }

    [Fact]
    public void EmoteStarPair_MarksWholeEmote()
    {
        ParseSingleRun(EmoteStar, "*waves*").Should().Equal(
            ("*waves*", SegmentType.Emote));
    }

    [Fact]
    public void EmptyRunText_YieldsEmptySpanList()
    {
        var result = Parse(SayQuotes, "");
        result.Should().HaveCount(1);
        result[0].Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Pass composition within one run
    // ------------------------------------------------------------------

    [Fact]
    public void SecondPass_OnlySubdividesUndefinedSpans()
    {
        const string text = "((\"q\")) \"r\"";
        var afterOoc = new SegmentParser(Ooc).Apply([text], InitialSpans([text]));
        var afterSay = new SegmentParser(SayQuotes).Apply([text], afterOoc);

        AssertFullCoverage(text, afterSay[0]);
        Render(text, afterSay[0]).Should().Equal(
            ("((\"q\"))", SegmentType.Ooc),
            (" ", SegmentType.Undefined),
            ("\"r\"", SegmentType.Say));
    }

    [Fact]
    public void OpenState_PersistsAcrossTypedSpans()
    {
        // The emote opened before the OOC block stays open across it and
        // closes after it — old Gobchat semantics.
        const string text = "a *b ((x)) c* d";
        var afterOoc = new SegmentParser(Ooc).Apply([text], InitialSpans([text]));
        var afterEmote = new SegmentParser(EmoteStar).Apply([text], afterOoc);

        AssertFullCoverage(text, afterEmote[0]);
        Render(text, afterEmote[0]).Should().Equal(
            ("a ", SegmentType.Undefined),
            ("*b ", SegmentType.Emote),
            ("((x))", SegmentType.Ooc),
            (" c*", SegmentType.Emote),
            (" d", SegmentType.Undefined));
    }

    [Fact]
    public void DelimiterState_DoesNotLeakBetweenApplyCalls()
    {
        var parser = new SegmentParser(SayQuotes);
        parser.Apply(["unclosed \"quote"], InitialSpans(["unclosed \"quote"]));

        var second = parser.Apply(["no quotes here"], InitialSpans(["no quotes here"]));
        Render("no quotes here", second[0]).Should().Equal(
            ("no quotes here", SegmentType.Undefined));
    }

    // ------------------------------------------------------------------
    // Cross-run state carry (delimiters spanning non-text payloads)
    // ------------------------------------------------------------------

    [Fact]
    public void OpenInFirstRun_ClosesInLaterRun()
    {
        // "quote opens → item link → quote closes": the link splits the text
        // into two runs; the open quote must carry across.
        var result = Parse(SayQuotes, "he said \"nice", " indeed\" yes");

        AssertFullCoverage("he said \"nice", result[0]);
        AssertFullCoverage(" indeed\" yes", result[1]);
        Render("he said \"nice", result[0]).Should().Equal(
            ("he said ", SegmentType.Undefined),
            ("\"nice", SegmentType.Say));
        Render(" indeed\" yes", result[1]).Should().Equal(
            (" indeed\"", SegmentType.Say),
            (" yes", SegmentType.Undefined));
    }

    [Fact]
    public void OpenState_CrossesFullyTypedRun()
    {
        string[] runs = ["say \"a", "((x))", "b\" end"];
        var afterOoc = new SegmentParser(Ooc).Apply(runs, InitialSpans(runs));
        var afterSay = new SegmentParser(SayQuotes).Apply(runs, afterOoc);

        Render(runs[0], afterSay[0]).Should().Equal(
            ("say ", SegmentType.Undefined),
            ("\"a", SegmentType.Say));
        Render(runs[1], afterSay[1]).Should().Equal(
            ("((x))", SegmentType.Ooc));
        Render(runs[2], afterSay[2]).Should().Equal(
            ("b\"", SegmentType.Say),
            (" end", SegmentType.Undefined));
    }

    [Fact]
    public void UnclosedAcrossRuns_MarksToEndOfMessage()
    {
        var result = Parse(SayQuotes, "a \"b", "c d");

        Render("a \"b", result[0]).Should().Equal(
            ("a ", SegmentType.Undefined),
            ("\"b", SegmentType.Say));
        Render("c d", result[1]).Should().Equal(
            ("c d", SegmentType.Say));
    }

    [Fact]
    public void TokenSplitAcrossRuns_DoesNotMatch()
    {
        // Documented limitation (same as old Gobchat): a multi-char token
        // interrupted by a payload boundary is not recognized.
        var result = Parse(Ooc, "before (", "( inside))");

        Render("before (", result[0]).Should().Equal(
            ("before (", SegmentType.Undefined));
        Render("( inside))", result[1]).Should().Equal(
            ("( inside))", SegmentType.Undefined));
    }
}
