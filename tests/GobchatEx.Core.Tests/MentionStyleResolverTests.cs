using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// <see cref="MentionStyleResolver"/> is the single place "override or default" gets decided.
/// WHY per-component fallback matters: a word styled with only a foreground must keep the default
/// glow (and vice versa) — matching the plugin-wide "0 = unset" color-picker convention — otherwise
/// setting one color would silently strip the other.
/// </summary>
public sealed class MentionStyleResolverTests
{
    private static readonly IReadOnlyList<(uint Foreground, uint Glow)> Styles =
        [(0xAA0000FF, 0x00BB00FF), (0, 0x00CC00FF), (0xDD0000FF, 0)];

    [Fact]
    public void StyleId_Zero_ReturnsDefault()
    {
        MentionStyleResolver.Resolve(0, Styles, 0x111111FF, 0x222222FF)
            .Should().Be((0x111111FFu, 0x222222FFu));
    }

    [Fact]
    public void StyleId_OutOfRange_ReturnsDefault()
    {
        MentionStyleResolver.Resolve(99, Styles, 0x111111FF, 0x222222FF)
            .Should().Be((0x111111FFu, 0x222222FFu));
    }

    [Fact]
    public void EmptyStylesTable_EveryIdDegradesToDefault()
    {
        // How the Mention style's own Enabled toggle turns off every per-word override at once:
        // the caller passes an empty table instead of gating each Resolve call individually.
        MentionStyleResolver.Resolve(1, [], 0x111111FF, 0x222222FF)
            .Should().Be((0x111111FFu, 0x222222FFu));
    }

    [Fact]
    public void FullOverride_BothComponentsWin()
    {
        MentionStyleResolver.Resolve(1, Styles, 0x111111FF, 0x222222FF)
            .Should().Be((0xAA0000FFu, 0x00BB00FFu));
    }

    [Fact]
    public void ForegroundOnlyOverride_KeepsDefaultGlow()
    {
        MentionStyleResolver.Resolve(3, Styles, 0x111111FF, 0x222222FF)
            .Should().Be((0xDD0000FFu, 0x222222FFu));
    }

    [Fact]
    public void GlowOnlyOverride_KeepsDefaultForeground()
    {
        MentionStyleResolver.Resolve(2, Styles, 0x111111FF, 0x222222FF)
            .Should().Be((0x111111FFu, 0x00CC00FFu));
    }
}
