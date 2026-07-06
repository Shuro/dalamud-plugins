using System;

namespace GobchatEx.Config;

/// <summary>General settings, persisted to general.json.</summary>
[Serializable]
public class GeneralConfig
{
    // Bump this and gate a load-time fixup on the old value when you make
    // backward-incompatible changes to this file's layout; each section
    // file versions independently.
    public int Version { get; set; } = 1;

    /// <summary>UI language for the plugin's own settings window (not in-game chat text).</summary>
    public LanguageOverride LanguageOverride { get; set; } = LanguageOverride.None;
}
