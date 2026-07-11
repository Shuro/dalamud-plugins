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

    /// <summary>Shows the Quickbar overlay (drag grip, feature toggles, settings/hide buttons).</summary>
    public bool ShowQuickbar { get; set; }

    // Quickbar hide conditions — names and semantics mirror Chat 2's display
    // options so the two bars behave alike; all default to hiding (Chat 2
    // ships loading screens/battle off). Evaluated every frame in
    // QuickbarWindow (DrawConditions/PreOpenCheck).

    /// <summary>Hides the Quickbar during cutscenes and group pose.</summary>
    public bool QuickbarHideDuringCutscenes { get; set; } = true;

    /// <summary>Hides the Quickbar while not logged in to a character.</summary>
    public bool QuickbarHideWhenNotLoggedIn { get; set; } = true;

    /// <summary>Hides the Quickbar while the game UI is hidden.</summary>
    public bool QuickbarHideWhenUiHidden { get; set; } = true;

    /// <summary>Hides the Quickbar during loading screens.</summary>
    public bool QuickbarHideInLoadingScreens { get; set; } = true;

    /// <summary>Hides the Quickbar while in combat.</summary>
    public bool QuickbarHideInBattle { get; set; } = true;

    /// <summary>
    /// Enables the legacy "/e gc ..." echo-command fallback from the pre-Dalamud standalone app, for
    /// old macros. Defaults on since it's a compatibility feature, not an opt-in extra.
    /// </summary>
    public bool LegacyEchoCommandFallback { get; set; } = true;
}
