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
        // The verb parser doesn't validate the name is present — ParseDistanceTarget reports an
        // empty target as null, so the handler can print the syntax error message.
        var verb = PlayerCommandVerbParser.Parse("distance");

        verb.Kind.Should().Be(PlayerCommandVerbKind.Distance);
        verb.Rest.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Bob", "Bob", null)]
    [InlineData("  Bob Smith  ", "Bob Smith", null)]
    [InlineData("Bob Smith [Zodiark]", "Bob Smith", "Zodiark")]
    [InlineData("Bob Smith[Zodiark]", "Bob Smith", "Zodiark")]
    [InlineData("K'hit Nunu-Nu", "K'hit Nunu-Nu", null)]
    [InlineData("Bob (alt)", "Bob (alt)", null)] // unsplittable text is taken whole as the name
    public void ParseDistanceTarget_SplitsNameAndWorld(string rest, string name, string? world)
    {
        PlayerCommandVerbParser.ParseDistanceTarget(rest).Should().Be((name, world));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseDistanceTarget_EmptyTarget_IsNull(string rest)
    {
        PlayerCommandVerbParser.ParseDistanceTarget(rest).Should().BeNull();
    }
}
