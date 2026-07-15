using System;
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
    // Also the on/off background of the Quickbar's feature buttons, so both
    // surfaces read as the same on/off state.
    internal static readonly Vector4 ToggleOnTrack = new(0.10f, 0.60f, 0.25f, 1f);
    internal static readonly Vector4 ToggleOnTrackHover = new(0.12f, 0.72f, 0.30f, 1f);
    internal static readonly Vector4 ToggleOffTrack = new(0.65f, 0.18f, 0.18f, 1f);
    internal static readonly Vector4 ToggleOffTrackHover = new(0.78f, 0.22f, 0.22f, 1f);

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

    /// <summary>
    /// Wrapped text with recolored sub-spans: TextWrapped can't change color mid-string, and
    /// TextColored + SameLine never wraps — so this walks the text as whitespace/word tokens,
    /// measures each, and starts a new line when the next word would cross
    /// <paramref name="wrapWidth"/>. Line breaks are only taken after whitespace, so a word
    /// split by a partial match (e.g. "shu" highlighted inside "shuro") stays together; a
    /// single word wider than the limit overflows, like ImGui's own wrapping. Spans must be
    /// sorted and non-overlapping (they come from the segmenter, which merges overlaps).
    /// Used by the mention tester and the mention history hover.
    /// </summary>
    public static void HighlightedTextWrapped(
        string text, IReadOnlyList<SegmentSpan> spans, Vector4 highlightColor, float wrapWidth)
    {
        // Zero item spacing: horizontal so adjacent tokens butt together seamlessly, vertical
        // so wrapped lines stack like TextWrapped's instead of like separate widgets.
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        var first = true;
        foreach (var (token, highlighted, newLine) in LayoutTokens(text, spans, wrapWidth))
        {
            if (!first && !newLine)
                ImGui.SameLine(0f, 0f);

            if (highlighted)
                ImGui.TextColored(highlightColor, token);
            else
                ImGui.TextUnformatted(token);

            first = false;
        }
    }

    /// <summary>How many lines <see cref="HighlightedTextWrapped"/> will draw for this input —
    /// for sizing a container before drawing (immediate mode has no draw-then-measure). 0 for
    /// empty text, exactly matching the renderer drawing nothing; callers reserve their own
    /// minimum height.</summary>
    public static int HighlightedTextLineCount(string text, IReadOnlyList<SegmentSpan> spans, float wrapWidth)
    {
        var lines = 0;
        foreach (var (_, _, newLine) in LayoutTokens(text, spans, wrapWidth))
        {
            if (newLine)
                ++lines;
        }

        return lines;
    }

    /// <summary>The shared wrap layout: each token tagged with whether it starts a new visual
    /// line. A token stays on its line when it is trailing whitespace (overflows invisibly), a
    /// glued word continuation (no break opportunity mid-word), or a word that still fits.</summary>
    private static IEnumerable<(string Token, bool Highlighted, bool NewLine)> LayoutTokens(
        string text, IReadOnlyList<SegmentSpan> spans, float wrapWidth)
    {
        var lineWidth = 0f;
        var first = true;
        var afterWhitespace = false;
        foreach (var (token, highlighted) in Tokenize(text, spans))
        {
            var width = ImGui.CalcTextSize(token).X;
            var isWhitespace = char.IsWhiteSpace(token[0]);
            var staysOnLine = !first && (isWhitespace || !afterWhitespace || lineWidth + width <= wrapWidth);
            lineWidth = staysOnLine ? lineWidth + width : width;

            yield return (token, highlighted, !staysOnLine);

            first = false;
            afterWhitespace = isWhitespace;
        }
    }

    /// <summary>Splits <paramref name="text"/> into (token, highlighted) pairs for
    /// <see cref="HighlightedTextWrapped"/>: highlight boundaries first, then maximal
    /// whitespace/non-whitespace runs within each region — concatenating all tokens yields
    /// the input exactly.</summary>
    private static IEnumerable<(string Token, bool Highlighted)> Tokenize(
        string text, IReadOnlyList<SegmentSpan> spans)
    {
        var position = 0;
        foreach (var span in spans)
        {
            foreach (var token in SplitTokens(text, position, span.Start))
                yield return (token, false);
            foreach (var token in SplitTokens(text, span.Start, span.End))
                yield return (token, true);
            position = span.End;
        }

        foreach (var token in SplitTokens(text, position, text.Length))
            yield return (token, false);
    }

    private static IEnumerable<string> SplitTokens(string text, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            var isWhitespace = char.IsWhiteSpace(text[i]);
            var j = i + 1;
            while (j < end && char.IsWhiteSpace(text[j]) == isWhitespace)
                ++j;
            yield return text[i..j];
            i = j;
        }
    }

    /// <summary>Matches <see cref="ToggleSwitch"/>'s width for layout math.</summary>
    public static float ToggleWidth() => ImGui.GetFrameHeight() * 1.55f;

    /// <summary>
    /// The rendered width of an <see cref="ImGuiComponents.IconButton(FontAwesomeIcon)"/> for
    /// this icon — glyph plus frame padding. Icon glyphs vary in width (FolderOpen is wider
    /// than frame height), so stretch-to-fill inputs measure the real buttons they share a
    /// row with instead of assuming square ones.
    /// </summary>
    public static float IconButtonWidth(FontAwesomeIcon icon)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.CalcTextSize(icon.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2f;
    }

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

    /// <summary>
    /// A removable-entry list flowing two entries per row (trash button + label, twice),
    /// so long lists take half the height of a single column. Entries fill row-major via
    /// the table's TableNextColumn wrap; an odd count leaves the last two cells empty.
    /// Returns the index whose trash button was clicked this frame, or -1 — the caller
    /// removes after the loop so the collection isn't mutated mid-draw.
    /// </summary>
    public static int RemovableListColumns(string id, int count, Func<int, string> label,
        string removeTooltip, ImGuiTableFlags extraFlags = ImGuiTableFlags.None)
    {
        var removed = -1;
        using var table = ImRaii.Table(id, 4, ImGuiTableFlags.SizingFixedFit | extraFlags);
        if (!table)
            return removed;

        ImGui.TableSetupColumn("##del-a", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##item-a", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##del-b", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##item-b", ImGuiTableColumnFlags.WidthStretch);

        for (var i = 0; i < count; ++i)
        {
            using var itemId = ImRaii.PushId(i);
            ImGui.TableNextColumn();
            if (DangerButton(FontAwesomeIcon.Trash, removeTooltip))
                removed = i;

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(label(i));
        }

        return removed;
    }
}
