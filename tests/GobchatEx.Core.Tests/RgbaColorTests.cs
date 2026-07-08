using System.Numerics;
using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// RgbaColor packs ImGui color vectors into the 0xRRGGBBAA wire format of Chat 2's message
/// styling IPC. WHY this matters: the byte order is a cross-plugin contract — swap R and A and
/// every group background renders as the wrong color (or as "no background", since 0 means
/// none) on the Chat 2 side, with no error anywhere. The packing rules are pinned here.
/// </summary>
public sealed class RgbaColorTests
{
    [Theory]
    [InlineData(1f, 0f, 0f, 1f, 0xFF0000FFu)] // opaque red: R in the top byte, A in the bottom
    [InlineData(0f, 1f, 0f, 1f, 0x00FF00FFu)]
    [InlineData(0f, 0f, 1f, 0.5f, 0x0000FF80u)]
    [InlineData(1f, 1f, 1f, 1f, 0xFFFFFFFFu)]
    public void FromVector4_PacksAsRrGgBbAa(float r, float g, float b, float a, uint expected)
    {
        RgbaColor.FromVector4(new Vector4(r, g, b, a)).Should().Be(expected);
    }

    [Fact]
    public void FromVector4_TransparentBlack_PacksToZero_TheNoColorSentinel()
    {
        // 0 is the IPC's "no background" — a fully transparent color and "none" must coincide.
        RgbaColor.FromVector4(Vector4.Zero).Should().Be(0u);
    }

    [Fact]
    public void FromVector4_ClampsComponentsOutsideZeroToOne()
    {
        // ImGui color edits can hand back out-of-range floats (HDR drag); the wire format is
        // 8 bits per channel, so components clamp instead of wrapping into other channels.
        RgbaColor.FromVector4(new Vector4(2f, -1f, 0.5f, 5f)).Should().Be(0xFF0080FFu);
    }

    [Fact]
    public void ToVector4_UnpacksTheSameChannelOrder()
    {
        var color = RgbaColor.ToVector4(0xFF000080);

        color.X.Should().BeApproximately(1f, 1e-6f);      // R
        color.Y.Should().BeApproximately(0f, 1e-6f);      // G
        color.Z.Should().BeApproximately(0f, 1e-6f);      // B
        color.W.Should().BeApproximately(128f / 255f, 1e-6f); // A
    }

    [Fact]
    public void RoundTrip_SurvivesForEveryByteValue()
    {
        // The settings UI round-trips group backgrounds through ToVector4 → ColorEdit4 →
        // FromVector4 every frame; any lossy channel would silently drift the saved color.
        for (var value = 0; value <= 255; value++)
        {
            var packed = (uint)((value << 24) | (value << 16) | (value << 8) | value);
            RgbaColor.FromVector4(RgbaColor.ToVector4(packed)).Should().Be(packed);
        }
    }

    [Fact]
    public void FromGameConfigColor_ReordersRrGgBbToRrGgBbAaWithOpaqueAlpha()
    {
        // GameConfig's low 24 bits are already 0xRRGGBB; only alpha needs adding.
        RgbaColor.FromGameConfigColor(0x123456u).Should().Be(0x123456FFu);
    }

    [Fact]
    public void FromGameConfigColor_IgnoresHighByte()
    {
        // Dalamud's GameConfig uint values can carry unrelated flag bits above the color —
        // only the low 24 bits are the RGB payload.
        RgbaColor.FromGameConfigColor(0xFF123456u).Should().Be(0x123456FFu);
    }

    [Fact]
    public void FromGameConfigColor_ZeroRgb_ReturnsNull_TheUnsetSentinel()
    {
        // WHY: 0 is Dalamud's "no color configured" convention (same reading Chat 2's
        // GetChannelColor uses) — misreading it as opaque black would silently render every
        // unconfigured channel in black instead of falling back to a sensible default.
        RgbaColor.FromGameConfigColor(0u).Should().BeNull();
    }
}
