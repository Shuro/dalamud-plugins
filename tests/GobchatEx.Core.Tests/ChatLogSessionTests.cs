using System;
using System.IO;
using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// ChatLogSession decides what gets written to which file — lazy file creation, one file per
/// login/character switch, drop-while-logged-out — without doing any I/O itself. WHY this matters:
/// these rules are the difference between "a tidy per-session archive" and "empty stray files or
/// RP lines written into the wrong character's log"; they must hold across login races and
/// settings edits, so they are pinned here against an injected clock.
/// </summary>
public sealed class ChatLogSessionTests
{
    private const string Folder = @"C:\logs";
    private DateTimeOffset now = new(2026, 7, 11, 21, 5, 0, TimeSpan.FromHours(2));

    private ChatLogSession CreateSession(bool useCharacterFolders = false)
    {
        var session = new ChatLogSession(() => now);
        session.Configure(Folder, useCharacterFolders);
        return session;
    }

    [Fact]
    public void Enqueue_WhileLoggedOut_IsDropped()
    {
        // Title-screen/system chatter before login must never produce a file.
        var session = CreateSession();
        session.Enqueue("dropped");
        session.PendingCount.Should().Be(0);
        session.DequeueWrite().Should().BeNull();
        session.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public void DequeueWrite_EmptyQueue_ReturnsNullAndCreatesNoPath()
    {
        // Lazy creation: merely being logged in with logging enabled must not name a file yet.
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.DequeueWrite().Should().BeNull();
        session.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public void DequeueWrite_FirstWrite_CreatesFileLazilyAndDrainsInOrder()
    {
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.Enqueue("a");
        session.Enqueue("b");

        var write = session.DequeueWrite();

        write.Should().NotBeNull();
        write!.FilePath.Should().Be(Path.Combine(Folder, "chatlog_2026-07-11_21-05_John-Gobchat.log"));
        write.IsNewFile.Should().BeTrue();
        write.Lines.Should().Equal("a", "b");
        session.PendingCount.Should().Be(0);
        session.CurrentFilePath.Should().Be(write.FilePath);
    }

    [Fact]
    public void DequeueWrite_SecondWrite_AppendsToSameFile()
    {
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.Enqueue("a");
        var first = session.DequeueWrite()!;

        now = now.AddMinutes(10); // later batch, same session -> same file despite new clock value
        session.Enqueue("b");
        var second = session.DequeueWrite()!;

        second.FilePath.Should().Be(first.FilePath);
        second.IsNewFile.Should().BeFalse();
    }

    [Fact]
    public void DequeueWrite_CharacterFoldersOn_NestsPerCharacterSubfolder()
    {
        var session = CreateSession(useCharacterFolders: true);
        session.SetCharacter("J'ohn Gobchat");
        session.Enqueue("a");

        session.DequeueWrite()!.FilePath.Should().Be(
            Path.Combine(Folder, "John Gobchat", "chatlog_2026-07-11_21-05_John-Gobchat.log"));
    }

    [Fact]
    public void DequeueWrite_UnsanitizableCharacterName_FallsBackToBaseFolder()
    {
        // A name that sanitizes away entirely must not produce Path.Combine(folder, "").
        var session = CreateSession(useCharacterFolders: true);
        session.SetCharacter("!!!");
        session.Enqueue("a");

        session.DequeueWrite()!.FilePath.Should().Be(
            Path.Combine(Folder, "chatlog_2026-07-11_21-05.log"));
    }

    [Fact]
    public void SetCharacter_SameName_KeepsCurrentFile()
    {
        // Dev hot-reload and defensive re-seeds pass the same name again; the session must not
        // rotate the file for that.
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.Enqueue("a");
        var path = session.DequeueWrite()!.FilePath;

        session.SetCharacter("John Gobchat");

        session.CurrentFilePath.Should().Be(path);
    }

    [Fact]
    public void SetCharacter_DifferentName_StartsNewFile()
    {
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.Enqueue("a");
        var first = session.DequeueWrite()!.FilePath;

        now = now.AddMinutes(3);
        session.SetCharacter("Other Name");
        session.CurrentFilePath.Should().BeNull(); // closed; next write opens fresh
        session.Enqueue("b");

        var second = session.DequeueWrite()!;
        second.IsNewFile.Should().BeTrue();
        second.FilePath.Should().NotBe(first);
        second.FilePath.Should().Be(Path.Combine(Folder, "chatlog_2026-07-11_21-08_Other-Name.log"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCharacter_NullOrWhitespace_MeansLoggedOut(string? name)
    {
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.SetCharacter(name);

        session.CharacterName.Should().BeNull();
        session.Enqueue("dropped");
        session.DequeueWrite().Should().BeNull();
    }

    [Fact]
    public void SetCharacter_TrimsName()
    {
        var session = CreateSession();
        session.SetCharacter("  John Gobchat  ");
        session.CharacterName.Should().Be("John Gobchat");
    }

    [Fact]
    public void FlushBeforeSwitch_PendingLinesLandInTheOldFile()
    {
        // The caller's contract: flush (DequeueWrite + write) before SetCharacter, so lines said
        // as one character never end up in the next character's file.
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.Enqueue("old line");
        var old = session.DequeueWrite()!;

        now = now.AddMinutes(1);
        session.SetCharacter("Other Name");
        session.Enqueue("new line");
        var fresh = session.DequeueWrite()!;

        old.Lines.Should().Equal("old line");
        fresh.Lines.Should().Equal("new line");
        fresh.FilePath.Should().NotBe(old.FilePath);
    }

    [Fact]
    public void Configure_Unchanged_KeepsCurrentFile()
    {
        // The settings window notifies on ANY section change; an unrelated edit (e.g. a color)
        // re-Configures with identical values and must not rotate the log file.
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.Enqueue("a");
        var path = session.DequeueWrite()!.FilePath;

        session.Configure(Folder, useCharacterFolders: false);

        session.CurrentFilePath.Should().Be(path);
    }

    [Theory]
    [InlineData(@"C:\other", false)] // folder changed
    [InlineData(Folder, true)]       // per-character toggle flipped
    public void Configure_Changed_StartsNewFileInNewLocation(string folder, bool useCharacterFolders)
    {
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.Enqueue("a");
        session.DequeueWrite();

        session.Configure(folder, useCharacterFolders);

        session.CurrentFilePath.Should().BeNull();
        session.Enqueue("b");
        var write = session.DequeueWrite()!;
        write.IsNewFile.Should().BeTrue();
        write.FilePath.Should().StartWith(folder == Folder
            ? Path.Combine(Folder, "John Gobchat")
            : folder);
    }

    [Fact]
    public void TryResumeFile_SameCharacterAndFolder_ContinuesPreviousFile()
    {
        // Reload resume: a plugin reload/update must keep appending to the file the previous
        // plugin lifetime was writing — not fragment one play session into a file per reload.
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        var previous = Path.Combine(Folder, "chatlog_2026-07-11_20-00_John-Gobchat.log");

        session.TryResumeFile(previous).Should().BeTrue();
        session.Enqueue("a");
        var write = session.DequeueWrite()!;

        write.FilePath.Should().Be(previous);
        write.IsNewFile.Should().BeFalse(); // appending to the old file, not starting a new one
    }

    [Fact]
    public void TryResumeFile_OutsideConfiguredFolder_IsIgnored()
    {
        // The resume marker may predate a folder change; a stale path must not pull new lines
        // into the old location — lazy naming inside the current folder wins.
        var session = CreateSession();
        session.SetCharacter("John Gobchat");

        session.TryResumeFile(@"C:\old-logs\chatlog_2026-07-11_20-00_John-Gobchat.log").Should().BeFalse();
        session.Enqueue("a");
        var write = session.DequeueWrite()!;

        write.IsNewFile.Should().BeTrue();
        write.FilePath.Should().Be(Path.Combine(Folder, "chatlog_2026-07-11_21-05_John-Gobchat.log"));
    }

    [Fact]
    public void TryResumeFile_WhileLoggedOut_IsIgnored()
    {
        // Resume only makes sense inside a login; without a character the drop-while-logged-out
        // rule stays authoritative.
        var session = CreateSession();

        session.TryResumeFile(Path.Combine(Folder, "chatlog_2026-07-11_20-00_John-Gobchat.log")).Should().BeFalse();

        session.CurrentFilePath.Should().BeNull();
    }

    [Fact]
    public void TryResumeFile_CharacterSwitchAfterResume_RotatesToNewFile()
    {
        // Rotation rules outrank resume: a resumed file must not survive a character switch, or
        // the next character's lines would land in the previous character's log.
        var session = CreateSession();
        session.SetCharacter("John Gobchat");
        session.TryResumeFile(Path.Combine(Folder, "chatlog_2026-07-11_20-00_John-Gobchat.log")).Should().BeTrue();

        session.SetCharacter("Other Name");

        session.CurrentFilePath.Should().BeNull();
    }
}
