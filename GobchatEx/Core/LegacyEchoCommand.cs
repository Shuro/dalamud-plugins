using System.Text.RegularExpressions;

namespace GobchatEx.Core;

/// <summary>
/// Matches the legacy "gc &lt;command&gt;" echo-command prefix (the pre-Dalamud standalone app's
/// "/e gc ..." macro form, minus the "/e " that FFXIV's own /echo command strips before the chat
/// message is built). Pure string matching, split out of the Dalamud-facing
/// GobchatEx.Chat.LegacyCommandListener per ADR 0002 so it's unit-testable; the SeString/payload
/// reconstruction that produces the input text stays Dalamud-side.
/// </summary>
public static class LegacyEchoCommand
{
    // Requires at least one space and a non-empty remainder: a bare "gc" is left unmatched rather
    // than routed to CommandRouter's empty-args branch (which opens the settings window) — popping
    // a window open from a passive background listener on an incomplete echo would be a surprising
    // side effect the user didn't consciously trigger, unlike typing "/gex" themselves.
    private static readonly Regex Pattern = new(
        @"^gc\s+(?<rest>\S.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <returns>The text after "gc ", or null if <paramref name="commandText"/> doesn't match.</returns>
    public static string? TryMatch(string commandText)
    {
        var match = Pattern.Match(commandText.Trim());
        return match.Success ? match.Groups["rest"].Value : null;
    }
}
