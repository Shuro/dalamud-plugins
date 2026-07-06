using System;

namespace GobchatEx.Config;

/// <summary>
/// Highlight style for one segment type. Colors are UIColor sheet row ids;
/// 0 means "do not recolor". Disabled types are not even parsed.
/// </summary>
[Serializable]
public class SegmentStyle
{
    public bool Enabled { get; set; } = true;
    public ushort Foreground { get; set; }
    public ushort Glow { get; set; }
}
