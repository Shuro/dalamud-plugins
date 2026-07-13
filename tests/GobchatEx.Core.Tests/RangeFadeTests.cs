using GobchatEx.Core;

namespace GobchatEx.Core.Tests;

/// <summary>
/// The range filter fades chat by sender distance, ported from the app's
/// ChatMessageActorDataSetter. WHY this matters: this is the exact mapping a roleplayer sees as
/// messages dim with distance — full visibility within the fade-out radius, a linear ramp to the
/// cut-off, then hidden. The native chat log has no per-line opacity, so the plugin additionally
/// quantizes partial visibility into a small number of darkened color steps; those bucket
/// boundaries are pinned here too.
/// </summary>
public sealed class RangeFadeTests
{
    [Theory]
    [InlineData(10f, 100)] // inside fade-out radius -> fully visible
    [InlineData(16f, 100)] // exactly at fade-out -> fully visible
    [InlineData(20f, 50)]  // midway between fade-out (16) and cut-off (24) -> 50%
    [InlineData(24f, 0)]   // exactly at cut-off -> hidden
    [InlineData(30f, 0)]   // beyond cut-off -> hidden
    public void CalculateVisibility_FadesLinearlyByDistance(float distance, int expectedVisibility)
    {
        RangeFade.CalculateVisibility(distance, fadeOutDistance: 16, cutOffDistance: 24)
            .Should().Be(expectedVisibility);
    }

    [Fact]
    public void CalculateVisibility_ZeroDistance_IsFullyVisible()
    {
        // Distance 0 is what an unresolvable sender produces upstream; it must never fade.
        RangeFade.CalculateVisibility(0f, fadeOutDistance: 16, cutOffDistance: 24)
            .Should().Be(100);
    }

    [Fact]
    public void CalculateVisibility_FadeOutNotBelowCutOff_StaysFullyVisibleInsideCutOff()
    {
        // Degenerate config (fade-out >= cut-off): avoid divide-by-zero / inversion, stay visible.
        RangeFade.CalculateVisibility(16f, fadeOutDistance: 16, cutOffDistance: 16)
            .Should().Be(100);
    }

    [Fact]
    public void CalculateVisibility_FadeOutNotBelowCutOff_StillHidesBeyondCutOff()
    {
        // The degenerate-config guard only disables the ramp, not the cut-off itself: fade-out ==
        // cut-off is a valid "hard cutoff, no fade" setup and must still suppress beyond it.
        RangeFade.CalculateVisibility(17f, fadeOutDistance: 16, cutOffDistance: 16)
            .Should().Be(0);
    }

    [Theory]
    [InlineData(99, 0)] // barely faded -> lightest step
    [InlineData(67, 0)]
    [InlineData(66, 1)]
    [InlineData(34, 1)]
    [InlineData(33, 2)]
    [InlineData(1, 2)]  // nearly hidden -> darkest step
    public void FadeStep_QuantizesPartialVisibilityIntoThreeSteps(int visibility, int expectedStep)
    {
        // Callers only ask for a step when 0 < visibility < 100 (100 renders normally, 0 is
        // suppressed outright), so the three steps split the open interval into equal thirds.
        RangeFade.FadeStep(visibility, stepCount: 3).Should().Be(expectedStep);
    }

    [Fact]
    public void FadeStep_SingleStep_AlwaysReturnsStepZero()
    {
        RangeFade.FadeStep(50, stepCount: 1).Should().Be(0);
    }

    // ------------------------------------------------------------------
    // RemapOpacity — Chat 2's start/end opacity curve (ported from the
    // app's RangeFilterPreview.opacityAtDistance). WHY: the partial range
    // lerps end→start instead of the old plain 0→100 ramp, so the fade a
    // roleplayer sees in Chat 2 begins at startOpacity and bottoms out at
    // endOpacity just before the render-hide.
    // ------------------------------------------------------------------

    [Fact]
    public void RemapOpacity_FullVisibility_StaysFullyOpaque()
    {
        // Fading hasn't started inside the fade-out radius, no matter the startOpacity.
        RangeFade.RemapOpacity(100, startOpacity: 80, endOpacity: 30).Should().Be(100);
    }

    [Fact]
    public void RemapOpacity_ZeroVisibility_StaysHidden()
    {
        // endOpacity must never leak into the beyond-cut-off render-hide outcome.
        RangeFade.RemapOpacity(0, startOpacity: 80, endOpacity: 30).Should().Be(0);
    }

    [Theory]
    [InlineData(50, 55)] // midway -> midway between end (30) and start (80)
    [InlineData(90, 75)]
    [InlineData(10, 35)]
    public void RemapOpacity_PartialVisibility_LerpsBetweenEndAndStart(int visibility, int expectedOpacity)
    {
        // Pins the exact ported formula: end + (start - end) * visibility / 100.
        RangeFade.RemapOpacity(visibility, startOpacity: 80, endOpacity: 30).Should().Be(expectedOpacity);
    }

    [Fact]
    public void RemapOpacity_JumpsFromFullToStartOpacity()
    {
        // The app's intentional discontinuity: opacity steps from 100% straight down to
        // ~startOpacity the moment fading begins. (49.5 rounds to 50 — Math.Round midpoints
        // go to even — so 30 + 50 = 80.)
        RangeFade.RemapOpacity(99, startOpacity: 80, endOpacity: 30).Should().Be(80);
    }

    [Fact]
    public void RemapOpacity_EqualStartAndEnd_IsConstant()
    {
        RangeFade.RemapOpacity(50, startOpacity: 60, endOpacity: 60).Should().Be(60);
    }

    [Fact]
    public void RemapOpacity_InvertedRange_StillInterpolates()
    {
        // start < end is deliberately not enforced (the app doesn't either); the lerp is
        // well-defined inverted — pinned so a future "fix" doesn't silently change behavior.
        RangeFade.RemapOpacity(50, startOpacity: 30, endOpacity: 80).Should().Be(55);
    }
}
