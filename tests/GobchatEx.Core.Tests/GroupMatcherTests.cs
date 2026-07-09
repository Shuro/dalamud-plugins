using GobchatEx.Core;
using static GobchatEx.Core.Tests.SpanTestHelpers;

namespace GobchatEx.Core.Tests;

public sealed class GroupMatcherTests
{
    private static GroupMember Bare(string player) => new(player, World: null);
    private static GroupMember WithWorld(string player, string world) => new(player, world);

    private static GroupRule Custom(string id, bool active, params GroupMember[] members)
        => new(id, active, FfGroup: null, Members: members);

    private static GroupRule Friend(string id, bool active, int ffGroup)
        => new(id, active, FfGroup: ffGroup, Members: []);

    [Fact]
    public void MemberNameMatch_ReturnsGroupId()
    {
        var groups = new[] { Custom("g-darya", active: true, Bare("Darya")) };

        GroupMatcher.FindGroup("Darya", senderWorld: null, friendGroupIndex: null, groups)
            .Should().Be("g-darya");
    }

    [Fact]
    public void NoMatchingMember_ReturnsNull()
    {
        var groups = new[] { Custom("g-darya", active: true, Bare("Darya")) };

        GroupMatcher.FindGroup("Someone", senderWorld: null, friendGroupIndex: null, groups)
            .Should().BeNull();
    }

    [Fact]
    public void InactiveGroup_IsSkippedEvenIfMemberMatches()
    {
        var groups = new[] { Custom("g-darya", active: false, Bare("Darya")) };

        GroupMatcher.FindGroup("Darya", senderWorld: null, friendGroupIndex: null, groups)
            .Should().BeNull();
    }

    [Fact]
    public void FirstActiveMatchInListOrder_Wins()
    {
        // Caller controls precedence via list order (customs before friend groups); the matcher itself
        // just returns the first active hit — this pins that a later, also-matching group never wins.
        var groups = new[]
        {
            Custom("g-first", active: true, Bare("Darya")),
            Custom("g-second", active: true, Bare("Darya")),
        };

        GroupMatcher.FindGroup("Darya", senderWorld: null, friendGroupIndex: null, groups)
            .Should().Be("g-first");
    }

    [Fact]
    public void MathBoldSenderName_MatchesPlainTextMember()
    {
        var groups = new[] { Custom("g-darya", active: true, Bare("Darya")) };

        GroupMatcher.FindGroup(ToMathBold("Darya"), senderWorld: null, friendGroupIndex: null, groups)
            .Should().Be("g-darya");
    }

    [Fact]
    public void CaseInsensitive_MemberAndSenderNameStillMatch()
    {
        var groups = new[] { Custom("g-darya", active: true, Bare("DaRyA")) };

        GroupMatcher.FindGroup("dARYA", senderWorld: null, friendGroupIndex: null, groups)
            .Should().Be("g-darya");
    }

    [Fact]
    public void CrossWorldSender_MatchesBareMember()
    {
        // A member added with no world must match them on any world.
        var groups = new[] { Custom("g-khada", active: true, Bare("Khada Iriq")) };

        GroupMatcher.FindGroup("Khada Iriq", senderWorld: "Balmung", friendGroupIndex: null, groups)
            .Should().Be("g-khada");
    }

    [Fact]
    public void CrossWorldSender_MatchesSameWorldMember()
    {
        var groups = new[] { Custom("g-khada", active: true, WithWorld("Khada Iriq", "Balmung")) };

        GroupMatcher.FindGroup("Khada Iriq", senderWorld: "Balmung", friendGroupIndex: null, groups)
            .Should().Be("g-khada");
    }

    [Fact]
    public void CrossWorldSender_DoesNotMatchDifferentWorldMember()
    {
        var groups = new[] { Custom("g-khada", active: true, WithWorld("Khada Iriq", "Gilgamesh")) };

        GroupMatcher.FindGroup("Khada Iriq", senderWorld: "Balmung", friendGroupIndex: null, groups)
            .Should().BeNull();
    }

    [Fact]
    public void WorldQualifiedMember_CaseInsensitiveWorldMatch()
    {
        var groups = new[] { Custom("g-khada", active: true, WithWorld("Khada Iriq", "BALMUNG")) };

        GroupMatcher.FindGroup("Khada Iriq", senderWorld: "balmung", friendGroupIndex: null, groups)
            .Should().Be("g-khada");
    }

    [Fact]
    public void FriendGroupIndex_MatchesConfiguredFfGroup()
    {
        var groups = new[] { Friend("g-star", active: true, ffGroup: 0) };

        GroupMatcher.FindGroup("Anyone", senderWorld: null, friendGroupIndex: 0, groups)
            .Should().Be("g-star");
    }

    [Fact]
    public void FriendGroupIndex_NoMatchWhenIndexDiffers()
    {
        var groups = new[] { Friend("g-star", active: true, ffGroup: 0) };

        GroupMatcher.FindGroup("Anyone", senderWorld: null, friendGroupIndex: 1, groups)
            .Should().BeNull();
    }

    [Fact]
    public void FriendGroupIndex_NoMatchWhenSenderHasNoFriendGroup()
    {
        var groups = new[] { Friend("g-star", active: true, ffGroup: 0) };

        GroupMatcher.FindGroup("Anyone", senderWorld: null, friendGroupIndex: null, groups)
            .Should().BeNull();
    }

    [Fact]
    public void CustomGroupBeforeFriendGroupInList_TakesPrecedence()
    {
        var groups = new[]
        {
            Custom("g-custom", active: true, Bare("Darya")),
            Friend("g-star", active: true, ffGroup: 0),
        };

        GroupMatcher.FindGroup("Darya", senderWorld: null, friendGroupIndex: 0, groups)
            .Should().Be("g-custom");
    }

    // IsMember is the single membership predicate behind every add/remove surface (native context
    // menu, Chat 2 menu, /gobchat group, settings tab). These pin that its semantics stay identical
    // to FindGroup's coloring semantics — if they drift, a menu can say "Remove from X" for a
    // sender chat never actually colors, or vice versa.

    [Fact]
    public void IsMember_BareMember_MatchesPlayerOnAnyWorld()
    {
        GroupMatcher.IsMember(Bare("Darya"), "Darya", playerWorld: "Balmung")
            .Should().BeTrue();
    }

    [Fact]
    public void IsMember_WorldQualifiedMember_MatchesSameWorld()
    {
        GroupMatcher.IsMember(WithWorld("Darya", "Balmung"), "Darya", playerWorld: "Balmung")
            .Should().BeTrue();
    }

    [Fact]
    public void IsMember_WorldQualifiedMember_DoesNotMatchDifferentWorld()
    {
        GroupMatcher.IsMember(WithWorld("Darya", "Balmung"), "Darya", playerWorld: "Gilgamesh")
            .Should().BeFalse();
    }

    [Fact]
    public void IsMember_WorldQualifiedMember_DoesNotMatchUnknownWorld()
    {
        // A null player world means "unknown", not "any world" — only a bare stored entry may
        // claim a player whose world could not be resolved.
        GroupMatcher.IsMember(WithWorld("Darya", "Balmung"), "Darya", playerWorld: null)
            .Should().BeFalse();
    }

    [Fact]
    public void IsMember_FoldsCaseAndFancyFont_LikeChatMatching()
    {
        // Same NFKC + case folding as FindGroup: a fancy-font name typed into /gobchat group must
        // still round-trip through the membership UIs as the same player.
        GroupMatcher.IsMember(Bare("DARYA"), ToMathBold("darya"), playerWorld: "Balmung")
            .Should().BeTrue();
    }
}
