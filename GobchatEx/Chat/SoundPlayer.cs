using FFXIVClientStructs.FFXIV.Client.UI;

namespace GobchatEx.Chat;

/// <summary>
/// Plays built-in chat sound effects with a cooldown. Volume follows the
/// game's own sound-effects mixer. Must be called from the framework thread
/// (chat handlers and the config UI both are). See ADR 0003.
/// </summary>
public sealed class SoundPlayer
{
    private long? _lastPlayedMs;

    public void TryPlay(int effect, int cooldownMs)
    {
        var now = System.Environment.TickCount64;
        if (_lastPlayedMs is { } last && now - last < cooldownMs)
            return;

        _lastPlayedMs = now;
        Play(effect);
    }

    /// <summary>Plays immediately; used by the config window's preview button.</summary>
    public static void Play(int effect)
    {
        if (effect is < GameSound.Min or > GameSound.Max)
            return;

        UIGlobals.PlayChatSoundEffect((uint)effect);
    }
}
