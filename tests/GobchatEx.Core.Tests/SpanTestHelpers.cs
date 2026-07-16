using System.Text;
using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

internal static class SpanTestHelpers
{
    /// <summary>
    /// Mirrors <see cref="MessageSegmenter"/>'s private <c>InitialSpans</c> seeding (one Undefined
    /// span tiling each non-empty run) — keep in sync.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<SegmentSpan>> InitialSpans(IReadOnlyList<string> runs)
        => runs.Select(IReadOnlyList<SegmentSpan> (r) => r.Length > 0
                ? [new SegmentSpan(0, r.Length, SegmentType.Undefined)]
                : [])
            .ToList();

    /// <summary>Mention rules with only whole-word triggers populated, at the default fuzzy level.</summary>
    public static MentionRules WholeWordRules(params string[] triggers)
        => new([.. triggers], [], [], FuzzyMatchLevel.Conservative);

    // Map ASCII letters to their Mathematical Sans-Serif Bold code points (each a surrogate pair) —
    // the headline real-world "fancy font" case ("𝗙𝗟𝗨𝗫" instead of "FLUX").
    public static string ToMathBold(string ascii)
    {
        var sb = new StringBuilder();
        foreach (var c in ascii)
        {
            if (c >= 'A' && c <= 'Z')
                sb.Append(char.ConvertFromUtf32(0x1D5D4 + (c - 'A')));
            else if (c >= 'a' && c <= 'z')
                sb.Append(char.ConvertFromUtf32(0x1D5EE + (c - 'a')));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    public static IReadOnlyList<IReadOnlyList<SegmentSpan>> Parse(TokenRule rule, params string[] runs)
        => new SegmentParser(rule).Apply(runs, InitialSpans(runs));

    /// <summary>Renders spans back to (substring, type) tuples for readable assertions.</summary>
    public static (string Text, SegmentType Type)[] Render(string run, IReadOnlyList<SegmentSpan> spans)
        => spans.Select(s => (run.Substring(s.Start, s.Length), s.Type)).ToArray();

    /// <summary>Like <see cref="Render"/>, but also exposes each span's StyleId — for tests
    /// asserting per-word color-override merge/priority behavior.</summary>
    public static (string Text, SegmentType Type, int StyleId)[] RenderStyled(string run, IReadOnlyList<SegmentSpan> spans)
        => spans.Select(s => (run.Substring(s.Start, s.Length), s.Type, s.StyleId)).ToArray();

    /// <summary>Every span list must tile [0, text.Length): ordered, gap-free, no empty spans.</summary>
    public static void AssertFullCoverage(string run, IReadOnlyList<SegmentSpan> spans)
    {
        var expectedStart = 0;
        foreach (var span in spans)
        {
            span.Start.Should().Be(expectedStart, "spans must be contiguous and ordered");
            span.Length.Should().BePositive("zero-length spans must not be emitted");
            expectedStart = span.End;
        }

        expectedStart.Should().Be(run.Length, "spans must cover the whole run text");
    }
}
