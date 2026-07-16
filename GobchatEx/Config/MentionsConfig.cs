using System;
using System.Collections.Generic;

namespace GobchatEx.Config;

/// <summary>Mention settings (Milestone 1), persisted to mentions.json.</summary>
[Serializable]
public class MentionsConfig : IAlertSoundSettings
{
    // Bumped from 1: MentionTriggers changed shape (List<string> -> List<MentionTrigger>, adding
    // a per-word color/glow override) — a breaking, non-migrated change (plugin unpublished; an
    // old mentions.json fails to deserialize and this section resets once).
    public int Version { get; set; } = 2;

    /// <summary>
    /// Master switch for the whole mentions feature: global trigger words and player-name
    /// mentions alike. Nested under this is <see cref="PlayerMentionsEnabled"/>, which only
    /// gates the player-name-derived subset.
    /// </summary>
    public bool MentionsEnabled { get; set; } = true;

    /// <summary>Case-insensitive whole words that count as mentions, each with an optional
    /// per-word color/glow override.</summary>
    public List<MentionTrigger> MentionTriggers { get; set; } = [];

    /// <summary>
    /// Master switch for player-name mentions (Milestone 1). Characters are added
    /// manually via the Mentions tab's add-current-character button (the app's login
    /// auto-learn was not ported); this only gates whether any of them are matched
    /// at all.
    /// </summary>
    public bool PlayerMentionsEnabled { get; set; } = true;

    /// <summary>Remembered characters and their per-character mention settings.</summary>
    public List<CharacterMentionSettings> Characters { get; set; } = [];

    public bool MentionSoundEnabled { get; set; }
    public int MentionSoundEffect { get; set; } = 2;    // <se.2>
    public int MentionSoundCooldownMs { get; set; } = 5000;
    public bool SuppressSoundFromSelf { get; set; } = true;

    /// <summary>
    /// Skip the mention highlight in messages the local player sent (outgoing Tells count
    /// as self like <see cref="SuppressSoundFromSelf"/>'s rule, but Echo is exempt — /echo
    /// is the designated mention test channel and always keeps the highlight). Mention
    /// detection itself still runs, so the sound decision stays independent.
    /// </summary>
    public bool SuppressHighlightFromSelf { get; set; } = true;

    /// <summary>
    /// Play <see cref="MentionSoundFilePath"/> instead of the built-in
    /// <see cref="MentionSoundEffect"/>. The game effect stays configured
    /// underneath — it is also the fallback when the file fails to play
    /// (ADR 0004).
    /// </summary>
    public bool MentionSoundUseCustomFile { get; set; }

    /// <summary>Absolute path to a .wav/.mp3/.ogg file.</summary>
    public string MentionSoundFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Playback volume for the custom file only (0–1); game sound effects
    /// follow the game's own sound-effects mixer instead.
    /// </summary>
    public float MentionSoundVolume { get; set; } = 1f;

    // IAlertSoundSettings maps onto the historical MentionSound* properties — explicit so the
    // serialized mentions.json keys stay unchanged (explicit implementations aren't serialized).
    bool IAlertSoundSettings.SoundUseCustomFile
    {
        get => MentionSoundUseCustomFile;
        set => MentionSoundUseCustomFile = value;
    }

    int IAlertSoundSettings.SoundEffect
    {
        get => MentionSoundEffect;
        set => MentionSoundEffect = value;
    }

    string IAlertSoundSettings.SoundFilePath
    {
        get => MentionSoundFilePath;
        set => MentionSoundFilePath = value;
    }

    float IAlertSoundSettings.SoundVolume
    {
        get => MentionSoundVolume;
        set => MentionSoundVolume = value;
    }
}
