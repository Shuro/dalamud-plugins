using Dalamud.Interface;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// One tab in the settings window. Tabs draw against the window's staged
/// (mutable) configuration copy and never call Save — persisting and
/// applying happens in SettingsWindow's footer.
/// </summary>
internal interface ISettingsTab
{
    string Name { get; }
    FontAwesomeIcon Icon { get; }
    void Draw();
}
