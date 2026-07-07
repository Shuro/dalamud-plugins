using System;

namespace GobchatEx.Config;

/// <summary>
/// Highlight style for one segment type. Colors are packed RGBA (0xRRGGBBAA, the same format as
/// <see cref="Core.RgbaColor"/>) rendered via a raw SeString Color/EdgeColor macro rather than a
/// UIColor sheet row; 0 means "do not recolor". Disabled types are not even parsed.
/// </summary>
[Serializable]
public class SegmentStyle
{
    public bool Enabled { get; set; } = true;
    public uint Foreground { get; set; }
    public uint Glow { get; set; }
}
