using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

public sealed class MentionCommandVerbParserTests
{
    [Theory]
    [InlineData("add hello", "hello")]
    [InlineData("ADD hello", "hello")]
    [InlineData("add two words", "two words")]
    public void Add_ParsesToAdd_WithWordAsRest(string args, string expectedRest)
    {
        var verb = MentionCommandVerbParser.Parse(args);

        verb.Kind.Should().Be(MentionCommandVerbKind.Add);
        verb.Rest.Should().Be(expectedRest);
    }

    [Theory]
    [InlineData("remove hello", "hello")]
    [InlineData("REMOVE hello", "hello")]
    public void Remove_ParsesToRemove_WithWordAsRest(string args, string expectedRest)
    {
        var verb = MentionCommandVerbParser.Parse(args);

        verb.Kind.Should().Be(MentionCommandVerbKind.Remove);
        verb.Rest.Should().Be(expectedRest);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("LIST")]
    public void List_ParsesToList(string args)
    {
        MentionCommandVerbParser.Parse(args).Kind.Should().Be(MentionCommandVerbKind.List);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void EmptyOrUnrecognizedVerb_ParsesToInvalid(string args)
    {
        MentionCommandVerbParser.Parse(args).Kind.Should().Be(MentionCommandVerbKind.Invalid);
    }

    [Fact]
    public void AddWithNoWord_ParsesToAdd_WithEmptyRest()
    {
        // The parser doesn't validate the word is present — ExecuteAdd's own empty-check
        // (MentionCommandHandler.cs) owns that, so it can print the syntax error message.
        var verb = MentionCommandVerbParser.Parse("add");

        verb.Kind.Should().Be(MentionCommandVerbKind.Add);
        verb.Rest.Should().BeEmpty();
    }

    [Fact]
    public void RemoveWithNoWord_ParsesToRemove_WithEmptyRest()
    {
        // Same contract as the add case: ExecuteRemove owns the empty-word syntax error.
        var verb = MentionCommandVerbParser.Parse("remove");

        verb.Kind.Should().Be(MentionCommandVerbKind.Remove);
        verb.Rest.Should().BeEmpty();
    }
}
