using GobchatEx.Core;
using static GobchatEx.Core.Tests.SpanTestHelpers;

namespace GobchatEx.Core.Tests;

public sealed class MessageSegmenterTests
{
    private static MessageSegmenter DefaultSegmenter(params string[] triggers)
        => new(DefaultRules.All, triggers);

    private static (string Text, SegmentType Type)[] SegmentSingleRun(
        MessageSegmenter segmenter, string text, out bool hasMention)
    {
        var result = segmenter.Segment([text]);
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
            [new TokenRule(SegmentType.Say, ["\""], ["\""])], []);
        sayOnly.Segment(["*emote* only"]).Should().BeNull();
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
    public void EmptyInput_ReturnsNull()
    {
        DefaultSegmenter().Segment([]).Should().BeNull();
        DefaultSegmenter().Segment([""]).Should().BeNull();
    }
}
