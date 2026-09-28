using System;
using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// ChatLogNaming turns character names into file-system-safe tokens and builds the per-session log
/// filename, ported from the app's ChatLoggerBase. WHY this matters: FFXIV names contain
/// apostrophes (Miqo'te) and users run non-English locales — the filename must stay valid on disk,
/// stable across cultures (invariant zero-padded timestamp), and human-readable so players can find
/// "their" log among many sessions.
/// </summary>
public sealed class ChatLogNamingTests
{
    [Theory]
    [InlineData("J'ohn Gobchat", "John-Gobchat")] // apostrophe dropped, space becomes hyphen
    [InlineData("A  -  B", "A-B")]                // whitespace/hyphen runs collapse to one hyphen
    [InlineData("-John-", "John")]                // no leading/trailing hyphen
    [InlineData("!!!", "")]                       // punctuation-only -> empty (no char suffix)
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void SanitizeForFileName_KeepsLettersDigitsAndSingleHyphens(string? name, string expected)
    {
        ChatLogNaming.SanitizeForFileName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("J'ohn Gobchat", "John Gobchat")] // apostrophe dropped, space kept
    [InlineData("  A   B  ", "A B")]              // whitespace runs collapse, trimmed
    [InlineData("Jean-Luc Picard", "Jean-Luc Picard")] // hyphen kept, same as the file name
    [InlineData("-John- ", "John")]               // no leading/trailing separator
    [InlineData("!!!", "")]                      // punctuation-only -> falls back to base folder
    [InlineData(null, "")]
    public void SanitizeForFolderName_KeepsLettersDigitsAndSingleSpaces(string? name, string expected)
    {
        ChatLogNaming.SanitizeForFolderName(name).Should().Be(expected);
    }

    [Fact]
    public void BuildFileName_WithCharacter_AppendsSanitizedName()
    {
        var now = new DateTimeOffset(2026, 7, 3, 9, 5, 0, TimeSpan.FromHours(2));
        ChatLogNaming.BuildFileName(now, "J'ohn Gobchat")
            .Should().Be("chatlog_2026-07-03_09-05_John-Gobchat.log");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!")] // sanitizes to empty -> same as no character
    public void BuildFileName_WithoutUsableCharacter_OmitsSuffix(string? name)
    {
        var now = new DateTimeOffset(2026, 7, 3, 9, 5, 0, TimeSpan.FromHours(2));
        ChatLogNaming.BuildFileName(now, name).Should().Be("chatlog_2026-07-03_09-05.log");
    }
}
