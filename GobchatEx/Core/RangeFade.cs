using System;

namespace GobchatEx.Core;

/// <summary>
/// Distance-based visibility for the range filter (Milestone 3), ported from the app's
/// ChatMessageActorDataSetter.CalculateVisibility: full visibility inside the fade-out radius, a
/// linear ramp down to the cut-off, hidden beyond it. Distances are linear in-game yalms. The
/// native chat log has no per-line opacity, so <see cref="FadeStep"/> additionally quantizes a
/// partial visibility into one of a few progressively darker color steps; the Dalamud layer maps
/// each step to a UIColor row.
/// </summary>
public static class RangeFade
{
    public const int MaxVisibility = 100;

    /// <summary>
    /// Calculates the visibility in a range of [0,100] based on the linear in-game distance in
    /// yalms. A fade-out at or above the cut-off is a valid "hard cutoff, no fade" configuration:
    /// the ramp is skipped (guarding the division) but the cut-off still hides.
    /// </summary>
    public static int CalculateVisibility(float distance, float fadeOutDistance, float cutOffDistance)
    {
        if (distance > cutOffDistance) return 0;
        if (distance < fadeOutDistance) return MaxVisibility;
        if (cutOffDistance <= fadeOutDistance) return MaxVisibility;
        var percentage = 1 - ((distance - fadeOutDistance) / (cutOffDistance - fadeOutDistance));
        return (int)Math.Round(MaxVisibility * percentage);
    }

    /// <summary>
    /// Maps a partial visibility (0 &lt; visibility &lt; 100 — callers render 100 normally and
    /// suppress 0 outright) to a 0-based darkening step, 0 being the lightest. The open interval
    /// is split into <paramref name="stepCount"/> equal buckets.
    /// </summary>
    public static int FadeStep(int visibility, int stepCount)
    {
        var step = (MaxVisibility - visibility) * stepCount / MaxVisibility;
        return Math.Min(step, stepCount - 1);
    }
}
