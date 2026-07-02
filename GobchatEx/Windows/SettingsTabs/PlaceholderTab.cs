using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Stand-in page for a feature that exists in the standalone GobchatEx app
/// but is not implemented in the plugin yet. Keeps the settings navigation
/// aligned with the app so future features slot into a stable structure.
/// </summary>
internal sealed class PlaceholderTab : ISettingsTab
{
    public string Name => Loc.Get(nameKey);
    public FontAwesomeIcon Icon { get; }

    private readonly string nameKey;
    private readonly string descriptionKey;

    public PlaceholderTab(string nameKey, FontAwesomeIcon icon, string descriptionKey)
    {
        this.nameKey = nameKey;
        Icon = icon;
        this.descriptionKey = descriptionKey;
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
            ImGui.TextUnformatted(Loc.Get(descriptionKey));

        ImGuiHelpers.ScaledDummy(10f);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGuiHelpers.CenteredText(Loc.Get("Placeholder_ComingSoon"));
    }
}
