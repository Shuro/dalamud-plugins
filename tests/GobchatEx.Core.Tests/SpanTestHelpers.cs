using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

internal static class SpanTestHelpers
{
    public static IReadOnlyList<IReadOnlyList<SegmentSpan>> InitialSpans(IReadOnlyList<string> runs)
        => runs.Select(IReadOnlyList<SegmentSpan> (r) => r.Length > 0
                ? [new SegmentSpan(0, r.Length, SegmentType.Undefined)]
                : [])
            .ToList();

    public static IReadOnlyList<IReadOnlyList<SegmentSpan>> Parse(TokenRule rule, params string[] runs)
        => new SegmentParser(rule).Apply(runs, InitialSpans(runs));

    /// <summary>Renders spans back to (substring, type) tuples for readable assertions.</summary>
    public static (string Text, SegmentType Type)[] Render(string run, IReadOnlyList<SegmentSpan> spans)
        => spans.Select(s => (run.Substring(s.Start, s.Length), s.Type)).ToArray();

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
