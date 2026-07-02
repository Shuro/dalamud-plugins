using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Lumina.Excel.Sheets;

namespace GobchatEx.Windows;

/// <summary>
/// Swatch picker over the game's UIColor sheet (pattern from ChatAlerts):
/// foreground colors decode from the Dark field, glow colors from Light,
/// both RGBA with zero-alpha rows skipped. Values are UIColor row ids;
/// 0 means "no recolor". Built once per window, not per frame.
/// </summary>
internal sealed class UiColorPicker
{
    private const int SwatchesPerRow = 10;

    private readonly List<(ushort Row, Vector4 Color)> _foreground = [];
    private readonly List<(ushort Row, Vector4 Color)> _glow = [];

    public UiColorPicker()
    {
        foreach (var color in Plugin.DataManager.GetExcelSheet<UIColor>())
        {
            AddIfVisible(_foreground, (ushort)color.RowId, color.Dark);
            AddIfVisible(_glow, (ushort)color.RowId, color.Light);
        }
    }

    private static void AddIfVisible(List<(ushort, Vector4)> list, ushort row, uint rgba)
    {
        var a = rgba & 255;
        if (a == 0)
            return;

        var b = (rgba >> 8) & 255;
        var g = (rgba >> 16) & 255;
        var r = (rgba >> 24) & 255;
        list.Add((row, new Vector4(r / 255f, g / 255f, b / 255f, a / 255f)));
    }

    /// <summary>
    /// Draws a preview swatch that opens a swatch-grid popup; right-click
    /// clears to 0. Returns true when the value changed.
    /// </summary>
    public bool Draw(string id, ref ushort value, bool glow)
    {
        var palette = glow ? _glow : _foreground;
        var current = Lookup(palette, value);
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
                    ? "No recolor. Click to choose a color."
                    : $"UIColor row {value}. Right-click to clear.");
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
                    ImGui.TextUnformatted($"UIColor row {row}");
            }
        }

        return changed;
    }

    private static Vector4 Lookup(List<(ushort Row, Vector4 Color)> palette, ushort value)
    {
        if (value == 0)
            return Vector4.Zero;

        foreach (var (row, color) in palette)
        {
            if (row == value)
                return color;
        }

        return Vector4.Zero;
    }
}
