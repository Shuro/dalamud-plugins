using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace GobchatEx.Chat;

/// <summary>
/// Raw SeString Color (0x13) / EdgeColor (0x14) macro envelopes carrying an arbitrary packed
/// color, bypassing the UIColor sheet entirely — proven against both vanilla chat and Chat 2's
/// renderer via the Debug page's custom-color probes. <see cref="ToOpaqueAarrggbb"/> takes colors
/// in <see cref="Core.RgbaColor"/>'s 0xRRGGBBAA config-storage format and reorders to the macro's
/// native 0xAARRGGBB with alpha forced opaque — vanilla ignores the alpha byte on both codes, so
/// production callers (which never expose an alpha picker for these) don't need to carry one
/// through. The Debug page's own alpha-testing probes pack their own arbitrary alpha via
/// <c>PackAarrggbb</c> (Debug-only member) directly instead, since they're deliberately
/// testing that byte.
/// </summary>
internal static class SeStringColorMacro
{
    public const byte ColorMacroCode = 0x13;
    public const byte EdgeColorMacroCode = 0x14;

    /// <summary>0xRRGGBBAA (RgbaColor's config format) -> 0xAARRGGBB, alpha forced opaque.</summary>
    public static uint ToOpaqueAarrggbb(uint rgbaColor) => 0xFF000000u | (rgbaColor >> 8);

#if DEBUG
    /// <summary>Packs an ImGui Vector4 (0..1 per channel) into 0xAARRGGBB, alpha included as-is.</summary>
    public static uint PackAarrggbb(System.Numerics.Vector4 color)
    {
        static uint Channel(float value) => (uint)(System.Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
        return (Channel(color.W) << 24) | (Channel(color.X) << 16) | (Channel(color.Y) << 8) | Channel(color.Z);
    }
#endif

    /// <summary>Full macro envelope: 0x02, code, length expression, value expression, 0x03.</summary>
    public static RawPayload MakeColorMacro(byte macroCode, uint packedAarrggbb)
    {
        var expression = EncodeIntExpression(packedAarrggbb);
        var chunk = new List<byte> { 0x02, macroCode };
        chunk.AddRange(EncodeIntExpression((uint)expression.Count));
        chunk.AddRange(expression);
        chunk.Add(0x03);
        return new RawPayload([.. chunk]);
    }

    /// <summary>color(stackcolor) — pops one push off the raw Color macro stack.</summary>
    public static RawPayload PopColor() => new([0x02, ColorMacroCode, 0x02, 0xEC, 0x03]);

    /// <summary>edgecolor(stackcolor) — pops one push off the raw EdgeColor macro stack.</summary>
    public static RawPayload PopEdgeColor() => new([0x02, EdgeColorMacroCode, 0x02, 0xEC, 0x03]);

    // SeString integer expression: values < 0xCF are a single byte (value + 1); larger
    // values get a marker byte (0xF0 | nonzero-byte mask, minus 1) followed by the nonzero
    // bytes of the big-endian value — zero bytes are skipped, never embedded.
    private static List<byte> EncodeIntExpression(uint value)
    {
        if (value < 0xCF)
            return [(byte)(value + 1)];

        var bytes = new List<byte> { 0 };
        var marker = 0xF0;
        for (var i = 3; i >= 0; i--)
        {
            var b = (byte)(value >> (8 * i));
            if (b == 0)
                continue;
            marker |= 1 << i;
            bytes.Add(b);
        }

        bytes[0] = (byte)(marker - 1);
        return bytes;
    }
}
