using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

internal sealed class GeneralTab : ISettingsTab
{
    public string Name => Loc.Get("General_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Cog;

    private readonly Configuration mutable;

    public GeneralTab(Configuration mutable)
    {
        this.mutable = mutable;
    }

    public void Draw()
    {
        var enabled = mutable.RpHighlightEnabled;
        if (SettingsUi.Toggle(Loc.Get("General_RpHighlighting_Name"), ref enabled))
            mutable.RpHighlightEnabled = enabled;
        ImGuiComponents.HelpMarker(Loc.Get("General_RpHighlighting_Tooltip"));

        var movable = mutable.IsConfigWindowMovable;
        if (SettingsUi.Toggle(Loc.Get("General_MovableWindow_Name"), ref movable))
            mutable.IsConfigWindowMovable = movable;
        ImGuiComponents.HelpMarker(Loc.Get("General_MovableWindow_Tooltip"));

        ImGuiHelpers.ScaledDummy(6f);
        DrawLanguage();
    }

    private void DrawLanguage()
    {
        ImGui.TextUnformatted(Loc.Get("General_Language_Name"));
        ImGuiComponents.HelpMarker(Loc.Get("General_Language_Tooltip"));

        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("##language", mutable.LanguageOverride.Name()))
        {
            if (combo)
            {
                foreach (var option in Enum.GetValues<LanguageOverride>())
                {
                    if (ImGui.Selectable(option.Name(), option == mutable.LanguageOverride))
                        mutable.LanguageOverride = option;
                }
            }
        }
    }
}
