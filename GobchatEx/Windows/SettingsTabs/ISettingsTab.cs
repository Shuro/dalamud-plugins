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

/// <summary>
/// A settings tab with a top-level enable/disable switch shown on its nav-rail row.
/// The switch reads/writes the tab's own staged config field directly.
/// </summary>
internal interface IToggleableTab : ISettingsTab
{
    bool Enabled { get; set; }
}
