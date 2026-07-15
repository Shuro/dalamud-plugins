namespace GobchatEx.Config;

/// <summary>
/// The alert-sound quartet shared by every sound-capable feature (mention alerts, per-group
/// alerts — ADR 0005): a game sound effect, or a custom audio file with its own volume. Lets
/// the shared settings editor (AlertSoundEditor) and SoundPlayer's alert path work against
/// mention and group config alike. PlayerGroup implements it directly; MentionsConfig maps its
/// historical MentionSound* JSON names onto it explicitly so the serialized keys stay unchanged.
/// </summary>
public interface IAlertSoundSettings
{
    /// <summary>Play <see cref="SoundFilePath"/> instead of the built-in
    /// <see cref="SoundEffect"/>. The game effect stays configured underneath — it is also the
    /// fallback when the file fails to play (ADR 0004).</summary>
    bool SoundUseCustomFile { get; set; }

    /// <summary>Built-in chat sound effect (&lt;se.1&gt;–&lt;se.16&gt;).</summary>
    int SoundEffect { get; set; }

    /// <summary>Absolute path to a .wav/.mp3/.ogg file.</summary>
    string SoundFilePath { get; set; }

    /// <summary>Playback volume for the custom file only (0–1); game sound effects follow the
    /// game's own sound-effects mixer instead.</summary>
    float SoundVolume { get; set; }
}
