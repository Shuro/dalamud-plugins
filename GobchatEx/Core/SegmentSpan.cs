namespace GobchatEx.Core;

/// <summary>
/// A typed, contiguous region of one text run. Span lists produced by the
/// segmentation pipeline always tile the whole run text: ordered, gap-free,
/// non-overlapping, no zero-length spans.
/// </summary>
/// <param name="StyleId">For a <see cref="SegmentType.Mention"/> span, the per-word color
/// override to resolve (see <see cref="MentionStyleResolver"/>); 0 means "no override, use the
/// default mention style". Meaningless for every other <see cref="SegmentType"/>.</param>
public readonly record struct SegmentSpan(int Start, int Length, SegmentType Type, int StyleId = 0)
{
    /// <summary>Exclusive end index.</summary>
    public int End => Start + Length;
}
