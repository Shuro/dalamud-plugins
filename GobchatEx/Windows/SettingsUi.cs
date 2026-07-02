using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;

namespace GobchatEx.Windows;

/// <summary>
/// Small shared widgets for the settings tabs: accent-colored section
/// headers (Dalamud's ImGui bindings have no SeparatorText) and labelled
/// toggle switches.
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
}
