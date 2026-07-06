using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;

namespace GobchatEx.Windows;

/// <summary>
/// Small shared widgets for the settings tabs: accent-colored section
/// headers (Dalamud's ImGui bindings have no SeparatorText), labelled
/// toggle switches, and Ctrl+Shift-gated destructive buttons.
/// </summary>
internal static class SettingsUi
{
    public static void SectionHeader(string label, string? help = null)
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, label);
        if (help != null)
            ImGuiComponents.HelpMarker(help);
        ImGui.Separator();
    }

    /// <summary>
    /// A ToggleButton switch with a text label to its right. Returns true
    /// when the value changed. The label itself is not click-sensitive.
    /// </summary>
    public static bool Toggle(string label, ref bool value)
    {
        var changed = ImGuiComponents.ToggleButton($"##{label}", ref value);
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
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

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using (ImRaii.Tooltip())
                ImGui.TextUnformatted(tooltip);
        }

        return clicked;
    }
}
