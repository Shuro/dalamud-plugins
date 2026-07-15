using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class CommandRouterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceArgs_RoutesToToggleSettings(string args)
    {
        CommandRouter.Parse(args).Kind.Should().Be(CommandRouteKind.ToggleSettings);
    }

    [Theory]
    [InlineData("group 1 add Bob")]
    [InlineData("GROUP 1 add Bob")]
    [InlineData("g 1 add Bob")]
    [InlineData("G 1 add Bob")]
    public void GroupOrItsAlias_RoutesToGroup_WithTextAfterFirstWordAsRest(string args)
    {
        var route = CommandRouter.Parse(args);

        route.Kind.Should().Be(CommandRouteKind.Group);
        route.Rest.Should().Be("1 add Bob");
    }

    [Theory]
    [InlineData("player list")]
    [InlineData("PLAYER list")]
    [InlineData("p list")]
    [InlineData("P list")]
    public void PlayerOrItsAlias_RoutesToPlayer_WithTextAfterFirstWordAsRest(string args)
    {
        var route = CommandRouter.Parse(args);

        route.Kind.Should().Be(CommandRouteKind.Player);
        route.Rest.Should().Be("list");
    }

    [Theory]
    [InlineData("mention add hello", "add hello")]
    [InlineData("MENTION add hello", "add hello")]
    public void Mention_RoutesToMention_WithTextAfterFirstWordAsRest(string args, string expectedRest)
    {
        var route = CommandRouter.Parse(args);

        route.Kind.Should().Be(CommandRouteKind.Mention);
        route.Rest.Should().Be(expectedRest);
    }

    [Theory]
    [InlineData("log start", "start")]
    [InlineData("LOG start", "start")]
    public void Log_RoutesToLog_WithTextAfterFirstWordAsRest(string args, string expectedRest)
    {
        var route = CommandRouter.Parse(args);

        route.Kind.Should().Be(CommandRouteKind.Log);
        route.Rest.Should().Be(expectedRest);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("HELP")]
    public void Help_RoutesToHelp(string args)
    {
        CommandRouter.Parse(args).Kind.Should().Be(CommandRouteKind.Help);
    }

    [Theory]
    [InlineData("config open")]
    [InlineData("CONFIG OPEN")]
    public void ConfigOpen_RoutesToConfigOpen(string args)
    {
        CommandRouter.Parse(args).Kind.Should().Be(CommandRouteKind.ConfigOpen);
    }

    [Fact]
    public void ConfigWithoutOpen_RoutesToUnknown_CarryingFullTrimmedInput()
    {
        var route = CommandRouter.Parse("config");

        route.Kind.Should().Be(CommandRouteKind.Unknown);
        route.Rest.Should().Be("config");
    }

    [Fact]
    public void UnrecognizedWord_RoutesToUnknown_CarryingFullTrimmedInput()
    {
        // Only leading whitespace is stripped (TrimStart, matching the original inline logic) —
        // trailing whitespace is deliberately part of what "the full trimmed input" means here.
        var route = CommandRouter.Parse("  bogus arg");

        route.Kind.Should().Be(CommandRouteKind.Unknown);
        route.Rest.Should().Be("bogus arg");
    }
}
