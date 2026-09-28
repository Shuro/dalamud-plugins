using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class GroupCommandParserTests
{
    private static GroupCommand? Parse(string args, params string[] existingGroups)
        => GroupCommandParser.Parse(args, name => existingGroups.Contains(name, StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData("list")]
    [InlineData("  LIST ")]
    public void List_IsRecognized(string args)
    {
        GroupCommandParser.IsList(args).Should().BeTrue();
    }

    [Theory]
    [InlineData("2 add Bob")]
    [InlineData("add 2 Bob")]
    [InlineData("  2   ADD   Bob  ")]
    public void NumericLocator_BothOrders_ResolveToIndex(string args)
    {
        var command = Parse(args);

        command.Should().NotBeNull();
        command!.Task.Should().Be(GroupCommandTask.Add);
        command.Index.Should().Be(2);
        command.GroupName.Should().BeNull();
        command.PlayerName.Should().Be("Bob");
        command.PlayerWorld.Should().BeNull();
    }

    [Theory]
    [InlineData("Team 2 add Bob", "Team 2")]
    [InlineData("Team2 add Bob", "Team2")]
    public void NameEndingInDigit_IsANameLocator_NotAnIndex(string args, string expectedName)
    {
        // Regression: the unanchored numeric form used to match "2 add Bob" inside the name
        // locator and edit group #2 instead.
        var command = Parse(args, expectedName);

        command.Should().NotBeNull();
        command!.Index.Should().BeNull();
        command.GroupName.Should().Be(expectedName);
        command.PlayerName.Should().Be("Bob");
    }

    [Fact]
    public void PlayerWithWorld_SplitsNameAndWorld()
    {
        var command = Parse("Friends remove Bob Smith [Zodiark]", "Friends");

        command.Should().NotBeNull();
        command!.Task.Should().Be(GroupCommandTask.Remove);
        command.GroupName.Should().Be("Friends");
        command.PlayerName.Should().Be("Bob Smith");
        command.PlayerWorld.Should().Be("Zodiark");
    }

    [Theory]
    [InlineData("1 clear")]
    [InlineData("clear 1")]
    public void Clear_NeedsNoPlayer(string args)
    {
        var command = Parse(args);

        command.Should().NotBeNull();
        command!.Task.Should().Be(GroupCommandTask.Clear);
        command.Index.Should().Be(1);
        command.PlayerName.Should().BeNull();
    }

    [Fact]
    public void GroupNameContainingTaskWord_PrefersTheExistingGroup()
    {
        var command = Parse("Night Clear Squad add Bob", "Night Clear Squad");

        command.Should().NotBeNull();
        command!.GroupName.Should().Be("Night Clear Squad");
        command.Task.Should().Be(GroupCommandTask.Add);
        command.PlayerName.Should().Be("Bob");
    }

    [Fact]
    public void UnknownNameLocator_FallsBackToLeftmostSplit_ForTheErrorMessage()
    {
        var command = Parse("Nobody add Bob");

        command.Should().NotBeNull();
        command!.Locator.Should().Be("Nobody");
        command.GroupName.Should().Be("Nobody");
    }

    [Fact]
    public void PlayerNameWithApostropheAndHyphen_IsKept()
    {
        Parse("1 add Y'shtola Rhul-Tia")!.PlayerName.Should().Be("Y'shtola Rhul-Tia");
    }

    [Theory]
    [InlineData("")]
    [InlineData("add")]
    [InlineData("MyGroup")]
    [InlineData("1 add Bob!")]
    public void Unparseable_ReturnsNull(string args)
    {
        Parse(args, "MyGroup").Should().BeNull();
    }

    [Fact]
    public void AddWithoutPlayer_ParsesWithNullPlayer_ForTheHandlerToReject()
    {
        var command = Parse("1 add");

        command.Should().NotBeNull();
        command!.PlayerName.Should().BeNull();
    }
}
