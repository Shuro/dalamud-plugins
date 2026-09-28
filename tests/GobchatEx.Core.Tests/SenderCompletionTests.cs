using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class SenderCompletionTests
{
    [Fact]
    public void KnownWorld_IsKeptAsIs()
    {
        SenderCompletion.Complete("Bob", "Zodiark", isSelf: true, "Me Myself", "Home", "Current")
            .Should().Be(("Bob", (string?)"Zodiark"));
    }

    [Fact]
    public void OwnMessage_BecomesLocalNameAndHomeWorld()
    {
        // Raw own-post text may carry a party-number prefix; the clean local name replaces it.
        SenderCompletion.Complete("1Me Myself", null, isSelf: true, "Me Myself", "Home", "Current")
            .Should().Be(("Me Myself", (string?)"Home"));
    }

    [Fact]
    public void OwnMessage_WithoutHomeWorld_FallsBackToCurrentWorld()
    {
        SenderCompletion.Complete("Me Myself", null, isSelf: true, "Me Myself", null, "Current")
            .Should().Be(("Me Myself", (string?)"Current"));
    }

    [Fact]
    public void OtherWorldlessSender_StandsOnCurrentWorld()
    {
        SenderCompletion.Complete("Bob", null, isSelf: false, "Me Myself", "Home", "Current")
            .Should().Be(("Bob", (string?)"Current"));
    }

    [Fact]
    public void Self_WithoutLocalName_IsTreatedAsOtherSender()
    {
        SenderCompletion.Complete("Bob", null, isSelf: true, "", "Home", "Current")
            .Should().Be(("Bob", (string?)"Current"));
    }
}
