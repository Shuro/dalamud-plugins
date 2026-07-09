using GobchatEx.Core;
using static GobchatEx.Core.Tests.SpanTestHelpers;

namespace GobchatEx.Core.Tests;

public sealed class MessageSegmenterTests
{
    private static MessageSegmenter DefaultSegmenter(params string[] triggers)
        => new(DefaultRules.All, WholeWordRules(triggers));

    private static (string Text, SegmentType Type)[] SegmentSingleRun(
        MessageSegmenter segmenter, string text, out bool hasMention,
        SegmentType defaultType = SegmentType.Undefined)
    {
        var result = segmenter.Segment([text], defaultType);
        result.Should().NotBeNull();
        hasMention = result!.HasMention;
        AssertFullCoverage(text, result.RunSpans[0]);
        return Render(text, result.RunSpans[0]);
    }

    [Fact]
    public void Precedence_OocBeatsSay()
    {
        SegmentSingleRun(DefaultSegmenter(), "((\"quoted ooc\"))", out _).Should().Equal(
            ("((\"quoted ooc\"))", SegmentType.Ooc));
    }

    [Fact]
    public void Precedence_StarEmoteBeatsAngleEmote()
    {
        SegmentSingleRun(DefaultSegmenter(), "*<text>*", out _).Should().Equal(
            ("*<text>*", SegmentType.Emote));
    }

    [Fact]
    public void CompoundMessage_AllTypesMarked()
    {
        SegmentSingleRun(DefaultSegmenter(), "he said \"hi\" and *waves* ((brb))", out var hasMention)
            .Should().Equal(
                ("he said ", SegmentType.Undefined),
                ("\"hi\"", SegmentType.Say),
                (" and ", SegmentType.Undefined),
                ("*waves*", SegmentType.Emote),
                (" ", SegmentType.Undefined),
                ("((brb))", SegmentType.Ooc));
        hasMention.Should().BeFalse();
    }

    [Fact]
    public void MentionInsideSay_SplitsTheSpan()
    {
        SegmentSingleRun(DefaultSegmenter("Alice"), "\"hi Alice\" yo", out var hasMention)
            .Should().Equal(
                ("\"hi ", SegmentType.Say),
                ("Alice", SegmentType.Mention),
                ("\"", SegmentType.Say),
                (" yo", SegmentType.Undefined));
        hasMention.Should().BeTrue();
    }

    [Fact]
    public void Mention_CanCrossSpanBoundaries()
    {
        // Trigger containing a quote character straddles the Say/Undefined
        // boundary; matching runs over the full run text, not per span.
        SegmentSingleRun(DefaultSegmenter("b\" c"), "\"b\" c", out var hasMention)
            .Should().Equal(
                ("\"", SegmentType.Say),
                ("b\" c", SegmentType.Mention));
        hasMention.Should().BeTrue();
    }

    [Fact]
    public void MentionOnly_StillProducesResult()
    {
        SegmentSingleRun(DefaultSegmenter("bob"), "plain bob text", out var hasMention)
            .Should().Equal(
                ("plain ", SegmentType.Undefined),
                ("bob", SegmentType.Mention),
                (" text", SegmentType.Undefined));
        hasMention.Should().BeTrue();
    }

    [Fact]
    public void NothingMatched_ReturnsNull()
    {
        DefaultSegmenter().Segment(["plain text, no markup"]).Should().BeNull();
        DefaultSegmenter("bob").Segment(["plain text, no markup"]).Should().BeNull();
    }

    [Fact]
    public void ExcludedRules_DoNotParse()
    {
        var sayOnly = new MessageSegmenter(
            [new TokenRule(SegmentType.Say, ["\""], ["\""])], WholeWordRules());
        sayOnly.Segment(["*emote* only"]).Should().BeNull();
    }

    // ------------------------------------------------------------------
    // DefaultRules table integrity: every delimiter pair in the static
    // table must actually type its span. WHY: the table is hand-entered
    // data (look-alike quote code points) — a data-entry error would
    // otherwise ship silently. Escapes name the exact code points.
    // ------------------------------------------------------------------

    [Fact]
    public void DefaultRules_AngleBrackets_MarkEmote()
    {
        SegmentSingleRun(DefaultSegmenter(), "he says <waves> hi", out _).Should().Equal(
            ("he says ", SegmentType.Undefined),
            ("<waves>", SegmentType.Emote),
            (" hi", SegmentType.Undefined));
    }

    [Fact]
    public void DefaultRules_GermanQuotes_MarkSay()
    {
        // „hallo“ — open U+201E, close U+201C.
        SegmentSingleRun(DefaultSegmenter(), "sie sagt „hallo“ ja", out _).Should().Equal(
            ("sie sagt ", SegmentType.Undefined),
            ("„hallo“", SegmentType.Say),
            (" ja", SegmentType.Undefined));
    }

    [Fact]
    public void DefaultRules_GermanOpenWithRightDoubleClose_MarksSayToEnd()
    {
        // „hallo” — open U+201E, close U+201D. The „…“ rule shares the open token and runs
        // first: it claims the „ and, finding no “, marks to end of message (unclosed
        // semantics). Pinned as-is: the quoted text is Say either way, which is what the
        // table entry is for — but trailing text rides along.
        SegmentSingleRun(DefaultSegmenter(), "sie sagt „hallo” ja", out _).Should().Equal(
            ("sie sagt ", SegmentType.Undefined),
            ("„hallo” ja", SegmentType.Say));
    }

    [Fact]
    public void DefaultRules_CurlyDoubleQuotes_MarkSay()
    {
        // “hello” — open U+201C, close U+201D.
        SegmentSingleRun(DefaultSegmenter(), "she says “hello” ok", out _).Should().Equal(
            ("she says ", SegmentType.Undefined),
            ("“hello”", SegmentType.Say),
            (" ok", SegmentType.Undefined));
    }

    [Fact]
    public void DefaultRules_GuillemetsInward_MarkSay()
    {
        // »hallo« — open U+00BB, close U+00AB.
        SegmentSingleRun(DefaultSegmenter(), "er sagt »hallo« ja", out _).Should().Equal(
            ("er sagt ", SegmentType.Undefined),
            ("»hallo«", SegmentType.Say),
            (" ja", SegmentType.Undefined));
    }

    [Fact]
    public void DefaultRules_GuillemetsOutward_MarkSay()
    {
        // «bonjour» — open U+00AB, close U+00BB. The earlier »…« rule opens on the closing
        // » and (unclosed) claims it to end of message; the «…» rule then types «bonjour.
        // Pinned as-is: the quoted text is Say either way — deleting the «…» entry from
        // the table would leave «bonjour Undefined and fail here.
        SegmentSingleRun(DefaultSegmenter(), "il dit «bonjour» oui", out _).Should().Equal(
            ("il dit ", SegmentType.Undefined),
            ("«bonjour", SegmentType.Say),
            ("» oui", SegmentType.Say));
    }

    [Fact]
    public void NoTokenRules_ReturnsNull()
    {
        // With every delimiter rule disabled (empty table) and no mention triggers, even a
        // markup-looking message must come back null — the caller's "leave the message
        // untouched" fast path.
        var none = new MessageSegmenter([], WholeWordRules());
        none.Segment(["he said \"hi\" and *waves*"]).Should().BeNull();
    }

    [Fact]
    public void CrossRun_QuoteSpansLinkPayload()
    {
        var result = DefaultSegmenter().Segment(["he said \"nice", " indeed\" yes"]);

        result.Should().NotBeNull();
        Render("he said \"nice", result!.RunSpans[0]).Should().Equal(
            ("he said ", SegmentType.Undefined),
            ("\"nice", SegmentType.Say));
        Render(" indeed\" yes", result.RunSpans[1]).Should().Equal(
            (" indeed\"", SegmentType.Say),
            (" yes", SegmentType.Undefined));
    }

    [Fact]
    public void CrossRun_EmptyMiddleRun_CarriesOpenState()
    {
        // A non-text payload can contribute an empty text run between two text runs; the
        // open quote must carry across it (empty run: empty span list, state untouched)
        // instead of resetting and leaving the second half unstyled.
        var result = DefaultSegmenter().Segment(["a \"b", "", "c\" d"]);

        result.Should().NotBeNull();
        result!.RunSpans[1].Should().BeEmpty();
        Render("a \"b", result.RunSpans[0]).Should().Equal(
            ("a ", SegmentType.Undefined),
            ("\"b", SegmentType.Say));
        Render("c\" d", result.RunSpans[2]).Should().Equal(
            ("c\"", SegmentType.Say),
            (" d", SegmentType.Undefined));
    }

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        DefaultSegmenter().Segment([]).Should().BeNull();
        DefaultSegmenter().Segment([""]).Should().BeNull();
    }

    [Fact]
    public void DefaultType_AppliesToOtherwiseUndefinedText()
    {
        // A /say (or /s) line has no quote marks of its own, but the channel already implies Say.
        SegmentSingleRun(DefaultSegmenter(), "plain text, no markup", out _, SegmentType.Say)
            .Should().Equal(("plain text, no markup", SegmentType.Say));
    }

    [Fact]
    public void DefaultType_DoesNotOverrideExplicitlyTypedSpans()
    {
        // OOC/Emote markup embedded in a /say line still wins; only the untyped leftover falls
        // back to the channel's default.
        SegmentSingleRun(DefaultSegmenter(), "he waves *hello* and ((brb))", out _, SegmentType.Say)
            .Should().Equal(
                ("he waves ", SegmentType.Say),
                ("*hello*", SegmentType.Emote),
                (" and ", SegmentType.Say),
                ("((brb))", SegmentType.Ooc));
    }

    [Fact]
    public void DefaultType_Undefined_LeavesUntypedTextUnclassified()
    {
        // The default parameter value must reproduce today's channel-agnostic behavior exactly.
        DefaultSegmenter().Segment(["plain text, no markup"], SegmentType.Undefined).Should().BeNull();
    }

    [Fact]
    public void MentionRules_PartialAndFuzzyOverlay_SplitTheSpan()
    {
        var rules = new MentionRules(
            WholeWords: [],
            PartialWords: ["Sam"],
            FuzzyWords: ["Elara"],
            FuzzyMatchLevel.Conservative);
        var segmenter = new MessageSegmenter(DefaultRules.All, rules);

        SegmentSingleRun(segmenter, "\"hi Samantha and Elora\" bye", out var hasMention)
            .Should().Equal(
                ("\"hi ", SegmentType.Say),
                ("Sam", SegmentType.Mention),
                ("antha and ", SegmentType.Say),
                ("Elora", SegmentType.Mention),
                ("\"", SegmentType.Say),
                (" bye", SegmentType.Undefined));
        hasMention.Should().BeTrue();
    }
}
