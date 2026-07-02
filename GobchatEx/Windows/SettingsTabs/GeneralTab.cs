using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace GobchatEx.Windows.SettingsTabs;

internal sealed class GeneralTab : ISettingsTab
{
    public string Name => "General";
    public FontAwesomeIcon Icon => FontAwesomeIcon.Cog;

    private readonly Configuration mutable;

    public GeneralTab(Configuration mutable)
    {
        this.mutable = mutable;
    }

    public void Draw()
    {
        var enabled = mutable.RpHighlightEnabled;
        if (SettingsUi.Toggle("Enable RP highlighting", ref enabled))
            mutable.RpHighlightEnabled = enabled;
        ImGuiComponents.HelpMarker("Colors quoted speech, emotes, OOC and mention words in the native chat log.");

        var movable = mutable.IsConfigWindowMovable;
        if (SettingsUi.Toggle("Movable Settings Window", ref movable))
            mutable.IsConfigWindowMovable = movable;
        ImGuiComponents.HelpMarker("Takes effect after Save.");
    }
}
