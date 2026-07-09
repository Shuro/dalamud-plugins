using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;

namespace GobchatEx.Chat;

/// <summary>
/// Range-fade dimming that keeps colors (Milestone 3). Two color families pass through here:
/// pre-existing UIColor-row payloads the game itself embeds (item/map links etc.) still get
/// remapped to the sheet row closest (RGB distance) to that color darkened by the fade step's
/// factor, via <see cref="DimRow"/> — unrelated to this plugin's own recoloring, so it stays
/// row-based. This plugin's own spans (RP highlighting, group names) are now raw RGBA <see
/// cref="SeStringColorMacro"/> pushes: <see cref="PayloadRewriter"/> already dims them via <see
/// cref="DimRgba"/> before building the macro, so <see cref="DimPayloads"/> only needs to
/// recognize and pass them through unmodified (tracking push/pop depth so the plain-text
/// detection below still works). Text outside any color span gets the caller-supplied
/// channel-native color (<see cref="ChatListener.ResolveChannelColor"/> — the player's own Log
/// Text Color for that channel, e.g. Yell's yellow, Shout's orange), darkened the same way this
/// plugin's own colors are, via <see cref="DimRgba"/> — so a plain, unformatted Yell or Shout
/// message keeps its own hue while fading, instead of collapsing to one shared grey regardless
/// of channel. Only used from the chat-message pass on the framework thread, so the caches are
/// deliberately lock-free.
/// </summary>
internal static class UiColorDimmer
{
    // Darkening factor per fade step, 0 = lightest — an exact no-op identity multiplier (the
    // Debug page's "Step 0" button relies on this; production never dims at step 0 itself).
    // Provisional picks; tune with the Debug page's Range dimming injection buttons if a step is
    // illegible on some chat theme.
    private static readonly float[] StepFactors = [1.0f, 0.84f, 0.68f, 0.52f, 0.36f, 0.20f];

    private static readonly Dictionary<(ushort Row, int Step), ushort> DimCache = new();
    private static List<(ushort Row, Vector3 Rgb)>? palette;

    /// <summary>
    /// Rewrites a payload list one fade step darker. Foreground and glow rows are remapped via
    /// <see cref="DimRow"/>; this plugin's own raw Color/EdgeColor macros pass through untouched
    /// (already pre-dimmed by the caller); text runs outside any color span (a foreground pop —
    /// row 0 or a raw <c>color(stackcolor)</c>) are wrapped in <paramref name="uncoloredColor"/>
    /// (the message's channel-native color, config-storage 0xRRGGBBAA, not yet dimmed — dimmed
    /// here via <see cref="DimRgba"/> and pushed as a raw Color macro, same as this plugin's own
    /// styles) instead of one shared grey, so plain text keeps that channel's own hue.
    /// </summary>
    public static List<Payload> DimPayloads(IReadOnlyList<Payload> payloads, int step, uint uncoloredColor)
    {
        var result = new List<Payload>(payloads.Count + 8);
        var foregroundDepth = 0;
        var dimmedUncolored = DimRgba(uncoloredColor, step);

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

                case RawPayload raw when IsColorMacro(raw, SeStringColorMacro.ColorMacroCode):
                    foregroundDepth += IsColorMacroPop(raw) ? -1 : 1;
                    foregroundDepth = Math.Max(0, foregroundDepth);
                    result.Add(payload);
                    break;

                case RawPayload raw when IsColorMacro(raw, SeStringColorMacro.EdgeColorMacroCode):
                    result.Add(payload);
                    break;

                // Deliberately one push/pop pair per payload rather than bracketing contiguous
                // depth-0 runs: colors must never wrap foreign payloads (the same invariant
                // PayloadRewriter documents), adjacent depth-0 text payloads are rare, and this
                // only runs on already-faded messages — coalescing would add run tracking for
                // no measurable win.
                case TextPayload { Text.Length: > 0 } when foregroundDepth == 0:
                {
                    result.Add(SeStringColorMacro.MakeColorMacro(SeStringColorMacro.ColorMacroCode, SeStringColorMacro.ToOpaqueAarrggbb(dimmedUncolored)));
                    result.Add(payload);
                    result.Add(SeStringColorMacro.PopColor());
                    break;
                }

                default:
                    result.Add(payload);
                    break;
            }
        }

        return result;
    }

    /// <summary>Darkens a packed 0xRRGGBBAA color by a fade step's factor; alpha untouched, 0 stays 0.</summary>
    public static uint DimRgba(uint rgbaColor, int step)
    {
        if (rgbaColor == 0)
            return 0;

        var factor = StepFactors[Math.Clamp(step, 0, StepFactors.Length - 1)];
        var r = (uint)(((rgbaColor >> 24) & 255) * factor);
        var g = (uint)(((rgbaColor >> 16) & 255) * factor);
        var b = (uint)(((rgbaColor >> 8) & 255) * factor);
        var a = rgbaColor & 255;
        return (r << 24) | (g << 16) | (b << 8) | a;
    }

    // Sniffs the macro code byte without decoding the packed value — SeStringColorMacro already
    // pre-dims before emission, so DimPayloads only needs to know "is this ours" for depth tracking.
    private static bool IsColorMacro(RawPayload raw, byte macroCode)
    {
        var data = raw.Data;
        return data.Length >= 2 && data[0] == 0x02 && data[1] == macroCode;
    }

    private static bool IsColorMacroPop(RawPayload raw)
    {
        var data = raw.Data;
        return data.Length == 5 && data[^2] == 0xEC;
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

    /// <summary>UIColor sheet as (row, RGB) pairs, decoded from the Dark field.</summary>
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
