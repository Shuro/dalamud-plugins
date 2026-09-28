using System;
using GobchatEx.Core.Util;

namespace GobchatEx.Core.Tests;

/// <summary>
/// PathSecurityUtil keeps file-system access inside an allowed root, ported from the app. WHY this
/// matters: the chat logger accepts a hand-editable folder path from chatlog.json; a relative path
/// must resolve inside the plugin config directory and never escape via <c>..</c> traversal
/// (CWE-22) — otherwise a crafted config could make the plugin write log files to arbitrary
/// locations. Comparisons are case-insensitive (Windows-only plugin).
/// </summary>
public sealed class PathSecurityUtilTests
{
    [Theory]
    [InlineData(@"C:\root", @"C:\root", true)]                  // the root itself counts as contained
    [InlineData(@"C:\root", @"C:\root\sub\file.log", true)]     // nested child
    [InlineData(@"C:\Root", @"c:\rOOt\sub", true)]              // case-insensitive (Windows)
    [InlineData(@"C:\root\", @"C:\root\sub", true)]             // trailing separator on root normalized
    [InlineData(@"C:\root", @"C:\rootSibling\x", false)]        // shared textual prefix is NOT containment
    [InlineData(@"C:\root", @"C:\other\x", false)]
    public void IsContainedIn_ChecksCanonicalContainment(string root, string candidate, bool expected)
    {
        PathSecurityUtil.IsContainedIn(root, candidate).Should().Be(expected);
    }

    [Fact]
    public void IsContainedIn_DotDotTraversal_IsNotContained()
    {
        // GetFullPath collapses the ".." before comparing, so a path that textually starts under
        // the root but climbs out of it is caught.
        PathSecurityUtil.IsContainedIn(@"C:\root", @"C:\root\..\evil").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, @"C:\x")]
    [InlineData("", @"C:\x")]
    public void IsContainedIn_MissingRoot_Throws(string? root, string candidate)
    {
        var act = () => PathSecurityUtil.IsContainedIn(root!, candidate);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsContainedIn_MissingCandidate_Throws()
    {
        var act = () => PathSecurityUtil.IsContainedIn(@"C:\root", "");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveWithin_RelativePath_ResolvesUnderRoot()
    {
        PathSecurityUtil.ResolveWithin(@"C:\root", "logs").Should().Be(@"C:\root\logs");
    }

    [Fact]
    public void ResolveWithin_AbsolutePathInsideRoot_ReturnsCanonicalPath()
    {
        PathSecurityUtil.ResolveWithin(@"C:\root", @"C:\root\sub\.\logs").Should().Be(@"C:\root\sub\logs");
    }

    [Fact]
    public void ResolveWithin_AbsolutePathOutsideRoot_Throws()
    {
        var act = () => PathSecurityUtil.ResolveWithin(@"C:\root", @"C:\evil");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void ResolveWithin_DotDotEscape_Throws()
    {
        // The CWE-22 case the util exists for: a relative path climbing out of the root.
        var act = () => PathSecurityUtil.ResolveWithin(@"C:\root", @"..\evil");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void ResolveWithin_RootRelativePath_Throws()
    {
        // "\evil" is rooted but not fully qualified — GetFullPath resolves it against the current
        // drive's root, outside C:\root. Callers route such paths here precisely because the
        // containment check must catch them (they'd escape a naive IsPathRooted "absolute" branch).
        var act = () => PathSecurityUtil.ResolveWithin(@"C:\root", @"\evil");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void ResolveWithin_MissingArguments_Throw()
    {
        FluentActions.Invoking(() => PathSecurityUtil.ResolveWithin("", "x"))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => PathSecurityUtil.ResolveWithin(@"C:\root", ""))
            .Should().Throw<ArgumentNullException>();
    }

    // ResolveLogFolder tests build their roots from the temp directory so they run on any OS.
    private static readonly string ConfigDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gex-config");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveLogFolder_Unconfigured_IsEmptyAndValid(string configured)
    {
        PathSecurityUtil.ResolveLogFolder(configured, ConfigDir, out var invalid).Should().BeEmpty();
        invalid.Should().BeFalse();
    }

    [Fact]
    public void ResolveLogFolder_Relative_ResolvesInsideConfigDir()
    {
        PathSecurityUtil.ResolveLogFolder(" logs ", ConfigDir, out var invalid)
            .Should().Be(System.IO.Path.Combine(ConfigDir, "logs"));
        invalid.Should().BeFalse();
    }

    [Fact]
    public void ResolveLogFolder_RelativeEscape_IsInvalid()
    {
        PathSecurityUtil.ResolveLogFolder(System.IO.Path.Combine("..", "elsewhere"), ConfigDir, out var invalid)
            .Should().BeEmpty();
        invalid.Should().BeTrue();
    }

    [Fact]
    public void ResolveLogFolder_FullyQualified_IsTrustedAnywhere()
    {
        var picked = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "picked", "logs");
        PathSecurityUtil.ResolveLogFolder(picked, ConfigDir, out var invalid).Should().Be(picked);
        invalid.Should().BeFalse();
    }
}
