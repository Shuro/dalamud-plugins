using GobchatEx.Core;
using static GobchatEx.Core.Tests.SpanTestHelpers;

namespace GobchatEx.Core.Tests;

public sealed class MentionMatcherTests
{
    private static (string Text, SegmentType Type)[] Find(string text, params string[] triggers)
        => Render(text, new MentionMatcher(triggers).FindMentions(text));

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
}
