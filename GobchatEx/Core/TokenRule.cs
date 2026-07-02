namespace GobchatEx.Core;

/// <summary>
/// One delimiter rule: text between any start token and any end token is
/// classified as <paramref name="Type"/>, delimiters included.
/// </summary>
public sealed record TokenRule(SegmentType Type, string[] StartTokens, string[] EndTokens);
