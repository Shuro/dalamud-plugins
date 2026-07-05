using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;

namespace GobchatEx.Chat;

/// <summary>
/// Range-fade dimming that keeps colors (Milestone 3): a UIColor row is remapped to the sheet
/// row closest (RGB distance) to that color darkened by the fade step's factor, so styled spans
/// (RP highlighting, group names, pre-colored link text) fade down in their own hue instead of
/// being flattened to grey. Text outside any color span gets the caller's grey step row, the
/// pre-existing fade rendering. Only used from the chat-message pass on the framework thread,
/// so the caches are deliberately lock-free.
/// </summary>
internal static class UiColorDimmer
{
    // Darkening factor per fade step, 0 = lightest. Provisional picks; tune with the Debug
    // page's Range dimming injection buttons if a step is illegible on some chat theme.
    private static readonly float[] StepFactors = [0.7f, 0.5f, 0.3f];

    private static readonly Dictionary<(ushort Row, int Step), ushort> DimCache = new();
    private static List<(ushort Row, Vector3 Rgb)>? palette;

    /// <summary>
    /// Rewrites a payload list one fade step darker. Foreground and glow rows are remapped via
    /// <see cref="DimRow"/>; text runs outside any color span (a UIForeground with row 0 pops
    /// the game's color stack) are wrapped in <paramref name="uncoloredRow"/>.
    /// </summary>
    public static List<Payload> DimPayloads(IReadOnlyList<Payload> payloads, int step, ushort uncoloredRow)
    {
        var result = new List<Payload>(payloads.Count + 8);
        var foregroundDepth = 0;

        foreach (var payload in payloads)
        {
            switch (payload)
            {
                case UIForegroundPayload { ColorKey: 0 }:
                    foregroundDepth = Math.Max(0, foregroundDepth - 1);
                    result.Add(payload);
                    break;

                case UIForegroundPayload foreground:
                    foregroundDepth++;
                    result.Add(new UIForegroundPayload(DimRow(foreground.ColorKey, step)));
                    break;

                case UIGlowPayload { ColorKey: 0 }:
                    result.Add(payload);
                    break;

                case UIGlowPayload glow:
                    result.Add(new UIGlowPayload(DimRow(glow.ColorKey, step)));
                    break;

                case TextPayload { Text.Length: > 0 } when foregroundDepth == 0:
                    result.Add(new UIForegroundPayload(uncoloredRow));
                    result.Add(payload);
                    result.Add(new UIForegroundPayload(0));
                    break;

                default:
                    result.Add(payload);
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// The sheet row nearest to <paramref name="row"/>'s color darkened by the step's factor.
    /// Memoized — the linear sheet scan runs once per distinct (row, step).
    /// </summary>
    public static ushort DimRow(ushort row, int step)
    {
        if (DimCache.TryGetValue((row, step), out var cached))
            return cached;

        var factor = StepFactors[Math.Clamp(step, 0, StepFactors.Length - 1)];
        var dimmed = FindNearest(RowRgb(row) * factor);
        DimCache[(row, step)] = dimmed;
        return dimmed;
    }

    /// <summary>
    /// The sheet row rendering closest to an arbitrary RGB color (components 0..1). Used by the
    /// Formatting tab's "import from game" to map the game's channel colors — plain RGB values —
    /// onto the UIColor rows SeString rewriting needs.
    /// </summary>
    internal static ushort NearestRow(Vector3 rgb) => FindNearest(rgb);

    private static Vector3 RowRgb(ushort row)
    {
        foreach (var (candidate, rgb) in Palette())
        {
            if (candidate == row)
                return rgb;
        }

        return new Vector3(0.5f); // unknown row: dim from mid-grey rather than throwing
    }

    private static ushort FindNearest(Vector3 target)
    {
        ushort best = 0;
        var bestDistance = float.MaxValue;

        foreach (var (row, rgb) in Palette())
        {
            var distance = Vector3.DistanceSquared(rgb, target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = row;
            }
        }

        return best;
    }

    /// <summary>UIColor sheet as (row, RGB) pairs — Dark field, same decoding as UiColorPicker.</summary>
    private static List<(ushort Row, Vector3 Rgb)> Palette()
    {
        if (palette != null)
            return palette;

        var rows = new List<(ushort, Vector3)>();
        foreach (var color in Plugin.DataManager.GetExcelSheet<UIColor>())
        {
            var rgba = color.Dark;
            if ((rgba & 255) == 0)
                continue; // fully transparent rows can't render text

            rows.Add(((ushort)color.RowId, new Vector3(
                ((rgba >> 24) & 255) / 255f,
                ((rgba >> 16) & 255) / 255f,
                ((rgba >> 8) & 255) / 255f)));
        }

        palette = rows;
        return palette;
    }
}
