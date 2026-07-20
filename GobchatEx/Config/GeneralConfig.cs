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

    /// <summary>
    /// Color scheme of the settings window itself (background + title bar):
    /// a Windows.SettingsWindowTheme.Id. Guid.Empty — the natural default —
    /// is the Dalamud Theme (follow the Dalamud theme); unknown ids fall
    /// back to it on lookup.
    /// </summary>
    public Guid WindowThemeId { get; set; }

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
    /// Hides the Quickbar while no chat window is visible (Chat 2 or the
    /// game's own chat log). Defaults on like the other hide options; turned
    /// off, the bar floats free when the chat disappears (the attach
    /// fallback behavior).
    /// </summary>
    public bool QuickbarHideWhenChatHidden { get; set; } = true;

    /// <summary>
    /// Glues the Quickbar to the top edge of the chat window — Chat 2's window
    /// when present, otherwise the game's own chat log. Evaluated every frame
    /// in QuickbarWindow.PreDraw; while no chat window is visible the bar
    /// floats free at its last position.
    /// </summary>
    public bool QuickbarAttachToChat { get; set; } = true;

    /// <summary>
    /// Enables the legacy "/e gc ..." echo-command fallback from the pre-Dalamud standalone app, for
    /// old macros. Defaults on since it's a compatibility feature, not an opt-in extra.
    /// </summary>
    public bool LegacyEchoCommandFallback { get; set; } = true;

    /// <summary>
    /// Changelog "seen" watermark — an index into the changelog window's entry list, not a plugin
    /// version. int.MaxValue (the default) means "nothing to show yet": both a genuinely fresh
    /// install and a config file saved before this feature existed start out already caught up,
    /// so upgrading to the version that introduces the changelog doesn't dump the whole backlog on
    /// existing users. Advances only when the changelog window is dismissed or opens with nothing
    /// new to show, per the configured ChangelogDisplayType.
    /// </summary>
    public int ChangelogLastSeenVersion { get; set; } = int.MaxValue;

    /// <summary>How eagerly the changelog window auto-opens on load: show every new entry, only
    /// entries flagged important, or never.</summary>
    public ChangelogDisplayType ChangelogDisplayType { get; set; } = ChangelogDisplayType.New;
}
