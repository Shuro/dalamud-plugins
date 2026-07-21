namespace GobchatEx.Chat;

/// <summary>
/// Packs this plugin's colors into the <c>0xAARRGGBB</c> form Lumina's
/// <c>SeStringBuilder.PushColorBgra</c>/<c>PushEdgeColorBgra</c> expects (the value the game's
/// Color/EdgeColor macros carry — alpha in the high byte). The builder owns the actual macro and
/// integer-expression encoding, so nothing here hand-rolls payload bytes anymore. Proven against
/// both vanilla chat and Chat 2's renderer via the Debug page's custom-color probes.
/// </summary>
internal static class ChatColor
{
    /// <summary>
    /// <see cref="Core.RgbaColor"/>'s config-storage <c>0xRRGGBBAA</c> -> <c>0xAARRGGBB</c>, alpha
    /// forced opaque — vanilla ignores the alpha byte on both Color and EdgeColor, and production
    /// callers never expose an alpha picker for these.
    /// </summary>
    public static uint ToOpaqueAarrggbb(uint rgbaColor) => 0xFF000000u | (rgbaColor >> 8);

#if DEBUG
    /// <summary>
    /// Packs an ImGui Vector4 (0..1 per channel) into <c>0xAARRGGBB</c>, alpha included as-is —
    /// the Debug page's probes deliberately test that the game ignores the alpha byte.
    /// </summary>
    public static uint PackAarrggbb(System.Numerics.Vector4 color)
    {
        static uint Channel(float value) => (uint)(System.Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
        return (Channel(color.W) << 24) | (Channel(color.X) << 16) | (Channel(color.Y) << 8) | Channel(color.Z);
    }
#endif
}
