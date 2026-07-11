using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class LegacyEchoCommandTests
{
    [Theory]
    [InlineData("gc group foo", "group foo")]
    [InlineData("GC group foo", "group foo")]
    [InlineData("Gc  player list", "player list")]
    [InlineData("  gc group foo  ", "group foo")]
    public void GcPrefixedText_MatchesAndReturnsTheRemainder(string commandText, string expectedRest)
    {
        LegacyEchoCommand.TryMatch(commandText).Should().Be(expectedRest);
    }

    [Theory]
    [InlineData("gc")]
    [InlineData("gc ")]
    [InlineData("gcfoo")]
    [InlineData("notgc group foo")]
    [InlineData("")]
    public void NonMatchingText_ReturnsNull(string commandText)
    {
        LegacyEchoCommand.TryMatch(commandText).Should().BeNull();
    }
}
