using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class CommandTextTests
{
    [Theory]
    [InlineData("", "", "")]
    [InlineData("   ", "", "")]
    [InlineData("list", "list", "")]
    [InlineData("  Add  hello world ", "Add", " hello world")]
    [InlineData("distance Bob [Zodiark]", "distance", "Bob [Zodiark]")]
    public void SplitVerb_SplitsTrimmedArgsAtFirstSpace(string args, string verb, string rest)
    {
        CommandText.SplitVerb(args).Should().Be((verb, rest));
    }
}
