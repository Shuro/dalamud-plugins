using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class LogCommandVerbParserTests
{
    [Theory]
    [InlineData("start")]
    [InlineData("START")]
    [InlineData("  start  ")]
    public void Start_ParsesToStart(string args)
    {
        LogCommandVerbParser.Parse(args).Should().Be(LogCommandVerbKind.Start);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("STOP")]
    public void Stop_ParsesToStop(string args)
    {
        LogCommandVerbParser.Parse(args).Should().Be(LogCommandVerbKind.Stop);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("STATUS")]
    public void Status_ParsesToStatus(string args)
    {
        LogCommandVerbParser.Parse(args).Should().Be(LogCommandVerbKind.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void EmptyOrUnrecognizedVerb_ParsesToInvalid(string args)
    {
        LogCommandVerbParser.Parse(args).Should().Be(LogCommandVerbKind.Invalid);
    }

    [Fact]
    public void TrailingTextAfterVerb_IsIgnored()
    {
        // No log verb takes arguments; extra text after the verb is ignored rather than
        // rejected, matching PlayerCommandVerbParser's count/list leniency.
        LogCommandVerbParser.Parse("start now please").Should().Be(LogCommandVerbKind.Start);
    }
}
