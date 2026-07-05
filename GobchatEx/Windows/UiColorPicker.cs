using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Localization;
using Lumina.Excel.Sheets;

namespace GobchatEx.Windows;

/// <summary>
/// Swatch picker over the game's UIColor sheet (pattern from ChatAlerts):
/// foreground colors decode from the Dark field, glow colors from Light,
/// both RGBA with zero-alpha rows skipped. Values are UIColor row ids;
/// 0 means "no recolor". Built once per window, not per frame. The sheet
/// itself has no color ordering, so the grid is sorted greys-first, then
/// by hue band, light to dark — raw row order reads as random noise. Rows
/// with identical values (black alone repeats a dozen+ times) are collapsed
/// to one swatch; the per-row maps keep every row previewable regardless.
/// </summary>
internal sealed class UiColorPicker
{
    private const int SwatchesPerRow = 10;

    private readonly List<(ushort Row, Vector4 Color)> _foreground = [];
    private readonly List<(ushort Row, Vector4 Color)> _glow = [];
    private readonly Dictionary<ushort, Vector4> _foregroundByRow = [];
    private readonly Dictionary<ushort, Vector4> _glowByRow = [];

    public UiColorPicker()
    {
        var seenForeground = new HashSet<uint>();
        var seenGlow = new HashSet<uint>();

        foreach (var color in Plugin.DataManager.GetExcelSheet<UIColor>())
        {
            AddIfVisible(_foreground, _foregroundByRow, seenForeground, (ushort)color.RowId, color.Dark);
            AddIfVisible(_glow, _glowByRow, seenGlow, (ushort)color.RowId, color.Light);
        }

        _foreground.Sort((a, b) => SortKey(a.Color).CompareTo(SortKey(b.Color)));
        _glow.Sort((a, b) => SortKey(a.Color).CompareTo(SortKey(b.Color)));
    }

    /// <summary>
    /// Greys first (band 0), then twelve hue bands around the color wheel; within a band,
    /// light before dark. Coarse on purpose — perfect perceptual sorting isn't the goal,
    /// finding "the orange one" at a glance is.
    /// </summary>
    private static int SortKey(Vector4 color)
    {
        var max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        var min = MathF.Min(color.X, MathF.Min(color.Y, color.Z));
        var delta = max - min;
        var saturation = max <= 0f ? 0f : delta / max;

        var band = 0;
        if (saturation >= 0.15f && delta > 0f)
        {
            float hue; // 0..6
            if (max == color.X)
                hue = ((color.Y - color.Z) / delta % 6f + 6f) % 6f;
            else if (max == color.Y)
                hue = (color.Z - color.X) / delta + 2f;
            else
                hue = (color.X - color.Y) / delta + 4f;

            band = 1 + (int)(hue * 2f);
        }

        return (band << 8) | (int)((1f - max) * 255f);
    }

    private static void AddIfVisible(
        List<(ushort, Vector4)> grid, Dictionary<ushort, Vector4> byRow, HashSet<uint> seen, ushort row, uint rgba)
    {
        var a = rgba & 255;
        if (a == 0)
            return;

        var b = (rgba >> 8) & 255;
        var g = (rgba >> 16) & 255;
        var r = (rgba >> 24) & 255;
        var color = new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);

        byRow[row] = color;
        if (seen.Add(rgba))
            grid.Add((row, color));
    }

    /// <summary>
    /// Draws a preview swatch that opens a swatch-grid popup; right-click
    /// clears to 0. Returns true when the value changed.
    /// </summary>
    public bool Draw(string id, ref ushort value, bool glow)
    {
        var palette = glow ? _glow : _foreground;
        var current = Lookup(glow, value);
        var changed = false;
        var swatchSize = new Vector2(ImGui.GetFrameHeight());

        if (ImGui.ColorButton($"##{id}-preview", current, ImGuiColorEditFlags.AlphaPreview, swatchSize))
            ImGui.OpenPopup($"##{id}-popup");
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && value != 0)
        {
            value = 0;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
                ImGui.TextUnformatted(value == 0
                    ? Loc.Get("ColorPicker_NoRecolor_Tooltip")
                    : string.Format(Loc.Get("ColorPicker_Recolor_Tooltip"), value));
        }

        using var popup = ImRaii.Popup($"##{id}-popup");
        if (!popup)
            return changed;

        var i = 0;
        foreach (var (row, color) in palette)
        {
            if (i++ % SwatchesPerRow != 0)
                ImGui.SameLine();

            if (ImGui.ColorButton($"##{id}-{row}", color, ImGuiColorEditFlags.AlphaPreview, swatchSize))
            {
                value = row;
                changed = true;
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted(string.Format(Loc.Get("ColorPicker_Swatch_Tooltip"), row));
            }
        }

        return changed;
    }

    // Preview lookup goes through the full per-row maps, not the deduplicated grid — a saved
    // value may sit on a row whose duplicate swatch was collapsed away.
    private Vector4 Lookup(bool glow, ushort value)
        => value == 0 ? Vector4.Zero : (glow ? _glowByRow : _foregroundByRow).GetValueOrDefault(value);
}
