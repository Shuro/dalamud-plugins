namespace GobchatEx.Core;

/// <summary>
/// A typed, contiguous region of one text run. Span lists produced by the
/// segmentation pipeline always tile the whole run text: ordered, gap-free,
/// non-overlapping, no zero-length spans.
/// </summary>
public readonly record struct SegmentSpan(int Start, int Length, SegmentType Type)
{
    /// <summary>Exclusive end index.</summary>
    public int End => Start + Length;
}
