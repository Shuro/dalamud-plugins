using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Windows;

/// <summary>
/// Small shared widgets for the settings tabs: accent-colored section
/// headers (Dalamud's ImGui bindings have no SeparatorText), labelled
/// green/red toggle switches, Ctrl+Shift-gated destructive buttons,
/// hover tooltips, and the 3-column channel checkbox grids.
/// </summary>
internal static class SettingsUi
{
    // Toggle track colors: green = on, red = off (hover variants slightly
    // brighter). Deliberately muted so a rail full of switches doesn't scream.
    private static readonly Vector4 ToggleOnTrack = new(0.10f, 0.60f, 0.25f, 1f);
    private static readonly Vector4 ToggleOnTrackHover = new(0.12f, 0.72f, 0.30f, 1f);
    private static readonly Vector4 ToggleOffTrack = new(0.65f, 0.18f, 0.18f, 1f);
    private static readonly Vector4 ToggleOffTrackHover = new(0.78f, 0.22f, 0.22f, 1f);

    public static void SectionHeader(string label, string? help = null)
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, label);
        if (help != null)
            ImGuiComponents.HelpMarker(help);
        ImGui.Separator();
    }

    /// <summary>
    /// An orange warning line — exclamation triangle plus wrapped text — for
    /// "feature unavailable" notices that should read as a warning instead of
    /// dimmed body text.
    /// </summary>
    public static void Warning(string text)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(ImGuiColors.DalamudOrange, FontAwesomeIcon.ExclamationTriangle.ToIconString());
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudOrange))
            ImGui.TextWrapped(text);
    }

    /// <summary>Matches <see cref="ToggleSwitch"/>'s width for layout math.</summary>
    public static float ToggleWidth() => ImGui.GetFrameHeight() * 1.55f;

    /// <summary>
    /// A toggle switch with a green (on) / red (off) track. Same geometry as
    /// <see cref="ImGuiComponents.ToggleButton"/>, drawn ourselves because
    /// Dalamud's hardcodes its gray track colors. Colors go through
    /// <see cref="ImGui.GetColorU32(Vector4)"/> so ImRaii.Disabled dims the
    /// switch like any other widget. Returns true when the value changed.
    /// </summary>
    public static bool ToggleSwitch(string id, ref bool value)
    {
        var p = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var height = ImGui.GetFrameHeight();
        var width = ToggleWidth();
        var radius = height * 0.50f;

        var changed = false;
        ImGui.InvisibleButton(id, new Vector2(width, height));
        if (ImGui.IsItemClicked())
        {
            value = !value;
            changed = true;
        }

        var track = ImGui.IsItemHovered()
            ? (value ? ToggleOnTrackHover : ToggleOffTrackHover)
            : (value ? ToggleOnTrack : ToggleOffTrack);

        drawList.AddRectFilled(p, new Vector2(p.X + width, p.Y + height),
            ImGui.GetColorU32(track), height * 0.50f);
        drawList.AddCircleFilled(
            new Vector2(p.X + radius + ((value ? 1 : 0) * (width - (radius * 2.0f))), p.Y + radius),
            radius - 1.5f, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));

        return changed;
    }

    /// <summary>
    /// A <see cref="ToggleSwitch"/> with a text label to its right. Returns
    /// true when the value changed. The label itself is not click-sensitive.
    /// </summary>
    public static bool Toggle(string label, ref bool value)
    {
        var changed = ToggleSwitch($"##{label}", ref value);
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        return changed;
    }

    /// <summary>
    /// A packed-RGBA (0xRRGGBBAA, <see cref="RgbaColor"/>) color swatch that opens ImGui's own
    /// picker popup on click — replaces the old FFXIV-named-palette swatch grid now that both
    /// vanilla and Chat 2 render arbitrary colors. Right-click clears to 0, the shared "no
    /// color" sentinel every packed-color field uses; the swatch previews that sentinel as a
    /// transparent checkerboard (real colors set through this widget are always fully opaque —
    /// see below — so a checkered swatch is unambiguously "unset", never an actual dark color).
    /// <paramref name="allowAlpha"/> false hides the alpha bar in the picker popup and forces
    /// the stored alpha byte to 0xFF: vanilla ignores alpha on the raw Color/EdgeColor macros
    /// Text/Glow render through, so there's nothing for the user to set there. Group backgrounds
    /// (Chat 2-only) pass true and keep full alpha — <paramref name="defaultAlpha"/> seeds the
    /// picker's starting alpha (only while <paramref name="value"/> is still the unset sentinel,
    /// i.e. the user hasn't picked a color yet) so a freshly-chosen background doesn't default to
    /// fully transparent and appear invisible; once a color is committed, further edits use its
    /// real stored alpha. <paramref name="tooltipOverride"/> replaces the default "no recolor /
    /// custom color" hover text — the Chat 2 background swatch uses it to explain the IPC
    /// connection state instead, including while wrapped in <see cref="ImRaii.Disabled"/> (the
    /// hover check below always allows disabled items through, which is a no-op when the widget
    /// isn't disabled). Returns true when the value changed.
    /// </summary>
    public static bool RgbaColorEdit(
        string id, ref uint value, bool allowAlpha, string? tooltipOverride = null, float defaultAlpha = 1f)
    {
        var swatchSize = new Vector2(ImGui.GetFrameHeight());
        var stored = RgbaColor.ToVector4(value);

        // The preview always reads the real (possibly zero) alpha via AlphaPreview, even when
        // allowAlpha is false — ImGui strips alpha preview flags whenever NoAlpha is also set,
        // so the "hide the alpha bar" edit-popup flags below can't also drive this preview.
        var changed = false;
        if (ImGui.ColorButton($"{id}-preview", stored, ImGuiColorEditFlags.AlphaPreview, swatchSize))
            ImGui.OpenPopup($"{id}-popup");

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && value != 0)
        {
            value = 0;
            changed = true;
        }

        Tooltip(tooltipOverride ?? (value == 0
            ? Loc.Get("ColorPicker_NoRecolor_Tooltip")
            : Loc.Get("ColorPicker_Recolor_Tooltip")));

        using (var popup = ImRaii.Popup($"{id}-popup"))
        {
            if (popup)
            {
                var edit = stored;
                if (!allowAlpha)
                    edit.W = 1f;
                else if (value == 0)
                    edit.W = defaultAlpha;

                var editFlags = allowAlpha ? ImGuiColorEditFlags.AlphaBar : ImGuiColorEditFlags.NoAlpha;
                if (ImGui.ColorPicker4($"{id}-picker", ref edit, editFlags))
                {
                    value = RgbaColor.FromVector4(edit);
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// A destructive icon-only action gated behind holding Ctrl+Shift, so a stray click can't
    /// lose data. Disabled — with a tooltip explaining the gesture — until both modifiers are
    /// held. Returns true only on a real click while the gate is open.
    /// </summary>
    public static bool DangerButton(FontAwesomeIcon icon, string tooltip)
        => DangerButtonCore(icon, null, tooltip);

    /// <summary>Icon+label variant of <see cref="DangerButton(FontAwesomeIcon, string)"/>.</summary>
    public static bool DangerButton(FontAwesomeIcon icon, string label, string tooltip)
        => DangerButtonCore(icon, label, tooltip);

    private static bool DangerButtonCore(FontAwesomeIcon icon, string? label, string tooltip)
    {
        var canActivate = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
        bool clicked;
        using (ImRaii.Disabled(!canActivate))
        {
            clicked = label is null
                ? ImGuiComponents.IconButton(icon)
                : ImGuiComponents.IconButtonWithText(icon, label);
        }

        Tooltip(tooltip);

        return clicked;
    }

    /// <summary>
    /// Standard hover tooltip for the previous item. Uses AllowWhenDisabled so it also shows
    /// while the item sits inside ImRaii.Disabled (a no-op for enabled items).
    /// </summary>
    public static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        using (ImRaii.Tooltip())
            ImGui.TextUnformatted(text);
    }

    /// <summary>
    /// A 3-column checkbox grid toggling each choice's channel in <paramref name="channels"/>
    /// (a live config list, mutated in place). A non-null HelpKey draws a help marker after
    /// that checkbox (the Range tab's engine-limit note).
    /// </summary>
    public static void ChannelGrid(
        string id, (string LabelKey, XivChatType Type, string? HelpKey)[] choices, List<XivChatType> channels)
    {
        using var table = ImRaii.Table(id, 3);
        if (!table)
            return;

        foreach (var (labelKey, type, helpKey) in choices)
        {
            ImGui.TableNextColumn();
            var active = channels.Contains(type);
            var changed = ImGui.Checkbox(Loc.Get(labelKey), ref active);
            if (helpKey != null)
                ImGuiComponents.HelpMarker(Loc.Get(helpKey));

            if (!changed)
                continue;

            if (active)
                channels.Add(type);
            else
                channels.Remove(type);
        }
    }

    /// <summary>Overload without per-item help markers (the Formatting tab's grids).</summary>
    public static void ChannelGrid(
        string id, (string LabelKey, XivChatType Type)[] choices, List<XivChatType> channels)
        => ChannelGrid(id, [.. choices.Select(c => (c.LabelKey, c.Type, (string?)null))], channels);
}
