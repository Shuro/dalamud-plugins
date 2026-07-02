using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Stand-in page for a feature that exists in the standalone GobchatEx app
/// but is not implemented in the plugin yet. Keeps the settings navigation
/// aligned with the app so future features slot into a stable structure.
/// </summary>
internal sealed class PlaceholderTab : ISettingsTab
{
    public string Name { get; }
    public FontAwesomeIcon Icon { get; }

    private readonly string description;

    public PlaceholderTab(string name, FontAwesomeIcon icon, string description)
    {
        Name = name;
        Icon = icon;
        this.description = description;
    }

    public void Draw()
    {
        ImGuiHelpers.ScaledDummy(20f);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var glyph = Icon.ToIconString();
            ImGuiHelpers.CenterCursorFor(ImGui.CalcTextSize(glyph).X);
            ImGui.TextUnformatted(glyph);
        }

        ImGuiHelpers.ScaledDummy(4f);
        ImGuiHelpers.CenteredText(Name);

        ImGuiHelpers.ScaledDummy(10f);
        using (ImRaii.TextWrapPos(0f))
            ImGui.TextUnformatted(description);

        ImGuiHelpers.ScaledDummy(10f);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGuiHelpers.CenteredText("Planned for a future release.");
    }
}
