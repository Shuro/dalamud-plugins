using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class PlayerCommandVerbParserTests
{
    [Theory]
    [InlineData("count")]
    [InlineData("COUNT")]
    [InlineData("  count  ")]
    public void Count_ParsesToCount(string args)
    {
        PlayerCommandVerbParser.Parse(args).Kind.Should().Be(PlayerCommandVerbKind.Count);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("LIST")]
    public void List_ParsesToList(string args)
    {
        PlayerCommandVerbParser.Parse(args).Kind.Should().Be(PlayerCommandVerbKind.List);
    }

    [Theory]
    [InlineData("distance Bob", "Bob")]
    [InlineData("DISTANCE Bob", "Bob")]
    [InlineData("distance Bob [World]", "Bob [World]")]
    public void Distance_ParsesToDistance_WithNameAndOptionalWorldAsRest(string args, string expectedRest)
    {
        var verb = PlayerCommandVerbParser.Parse(args);

        verb.Kind.Should().Be(PlayerCommandVerbKind.Distance);
        verb.Rest.Should().Be(expectedRest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void EmptyOrUnrecognizedVerb_ParsesToInvalid(string args)
    {
        PlayerCommandVerbParser.Parse(args).Kind.Should().Be(PlayerCommandVerbKind.Invalid);
    }

    [Fact]
    public void DistanceWithNoName_ParsesToDistance_WithEmptyRest()
    {
        // The parser doesn't validate the name is present — ExecuteDistance's own empty-check
        // (PlayerCommandHandler.cs) owns that, so it can print the syntax error message.
        var verb = PlayerCommandVerbParser.Parse("distance");

        verb.Kind.Should().Be(PlayerCommandVerbKind.Distance);
        verb.Rest.Should().BeEmpty();
    }
}
