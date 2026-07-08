using System;
using System.Numerics;

namespace GobchatEx.Core;

/// <summary>
/// 0xRRGGBBAA packing, the wire format of Chat 2's message styling IPC; 0 means "no color".
/// Distinct from the UIColor sheet row ids used for native-chat recoloring — these are literal
/// RGBA values.
/// </summary>
public static class RgbaColor
{
    public static uint FromVector4(Vector4 color)
        => ((uint)Math.Round(Math.Clamp(color.X, 0f, 1f) * 255) << 24)
           | ((uint)Math.Round(Math.Clamp(color.Y, 0f, 1f) * 255) << 16)
           | ((uint)Math.Round(Math.Clamp(color.Z, 0f, 1f) * 255) << 8)
           | (uint)Math.Round(Math.Clamp(color.W, 0f, 1f) * 255);

    public static Vector4 ToVector4(uint rgba) => new(
        ((rgba >> 24) & 255) / 255f,
        ((rgba >> 16) & 255) / 255f,
        ((rgba >> 8) & 255) / 255f,
        (rgba & 255) / 255f);

    /// <summary>
    /// Converts a Dalamud <c>IGameConfig</c> chat-color value (low 24 bits 0xRRGGBB, e.g.
    /// UiConfigOption.ColorSay) to this type's 0xRRGGBBAA format, alpha forced opaque. Null when
    /// unset (rgb == 0) — Dalamud's GameConfig convention for "no color configured", same reading
    /// Chat 2's GetChannelColor uses.
    /// </summary>
    public static uint? FromGameConfigColor(uint raw)
    {
        var rgb = raw & 0xFFFFFFu;
        return rgb == 0 ? null : (rgb << 8) | 0xFF;
    }
}
