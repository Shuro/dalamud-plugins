using System;
using System.IO;
using System.Runtime.InteropServices;
using Concentus;
using Concentus.Oggfile;
using FFXIVClientStructs.FFXIV.Client.UI;
using GobchatEx.Config;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GobchatEx.Chat;

/// <summary>
/// Plays the mention alert with a cooldown: a built-in chat sound effect
/// (volume follows the game's own sound-effects mixer) or a custom audio
/// file via NAudio (own volume, ADR 0004). Must be called from the framework
/// thread (chat handlers and the config UI both are). The file is loaded
/// lazily on first play and kept until the path changes, so a settings edit
/// costs one file read on the next alert; a failed file play logs, falls
/// back to the game effect and retries the file on the following alert.
/// </summary>
public sealed class SoundPlayer : IDisposable
{
    private long? _lastPlayedMs;

    private WaveStream? _reader;
    private VolumeSampleProvider? _volume;
    private WaveOutEvent? _output;
    private string? _loadedPath;

    public void TryPlay(MentionsConfig config)
    {
        var now = Environment.TickCount64;
        if (_lastPlayedMs is { } last && now - last < config.MentionSoundCooldownMs)
        {
            Plugin.Log.Debug("Mention sound suppressed: cooldown ({RemainingMs} ms left)",
                config.MentionSoundCooldownMs - (now - last));
            return;
        }

        _lastPlayedMs = now;

        if (config.MentionSoundUseCustomFile && PlayFile(config.MentionSoundFilePath, config.MentionSoundVolume))
            return;

        Play(config.MentionSoundEffect);
    }

    /// <summary>Plays a game effect immediately; used by the config window's preview button.</summary>
    public static void Play(int effect)
    {
        if (effect is < GameSound.Min or > GameSound.Max)
            return;

        UIGlobals.PlayChatSoundEffect((uint)effect);
    }

    /// <summary>
    /// Plays the file immediately (no cooldown) — the alert path after its
    /// cooldown check, and the config window's preview button. True when
    /// playback started; false lets the caller fall back to the game effect.
    /// </summary>
    public bool PlayFile(string path, float volume)
    {
        // Null-safe: Newtonsoft happily writes null into the non-nullable
        // config string from a hand-edited mentions.json.
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            if (_output == null || _loadedPath != path)
                LoadFile(path);
            // Clamped here rather than trusting the config: the UI caps at
            // 100 %, but a hand-edited value must not blast clipped audio.
            _volume!.Volume = Math.Clamp(volume, 0f, 1f);

            _output!.Stop();
            _reader!.Position = 0;
            _output.Play();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Playing mention sound file {Path} failed; the game sound effect plays instead", path);
            DisposeAudio();
            return false;
        }
    }

    private void LoadFile(string path)
    {
        DisposeAudio();

        // ToSampleProvider converts whatever the reader emits (float for
        // wav/mp3/vorbis, 16-bit PCM for opus) into the float samples the
        // volume wrapper expects, so every format shares one playback chain.
        _reader = CreateReader(path);
        _volume = new VolumeSampleProvider(_reader.ToSampleProvider());
        _output = new WaveOutEvent();
        _output.Init(_volume);
        _loadedPath = path;
    }

    /// <summary>
    /// Reads the file's play length without building a player — feeds the
    /// Mentions tab's too-long warning. Opus is probed via the Ogg page
    /// granule count instead of <see cref="CreateReader"/>, which would
    /// decode the whole file just to measure it. Null when the file can't
    /// be read; the real error surfaces (and falls back) on play.
    /// </summary>
    public static TimeSpan? GetDuration(string path)
    {
        try
        {
            if (IsOgg(path) && IsOggOpus(path))
            {
                using var file = File.OpenRead(path);
                OpusCodecFactory.AttemptToUseNativeLibrary = false;
                return new OpusOggReadStream(OpusCodecFactory.CreateDecoder(OpusSampleRate, OpusChannels), file).TotalTime;
            }

            using var reader = CreateReader(path);
            return reader.TotalTime;
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Probing the duration of {Path} failed", path);
            return null;
        }
    }

    private static bool IsOgg(string path) =>
        Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase);

    private static WaveStream CreateReader(string path)
    {
        if (!IsOgg(path))
            return new AudioFileReader(path);

        // An .ogg container holds either Vorbis or Opus (what Discord saves);
        // NVorbis only decodes the former, so pick by the codec marker on the
        // first Ogg page instead of trusting the extension. Windows' own ogg
        // codecs are an optional store package, hence two managed decoders.
        return IsOggOpus(path) ? DecodeOpus(path) : new VorbisWaveReader(path);
    }

    /// <summary>"OpusHead" sits in the first Ogg page's body; 512 bytes cover any sane header layout.</summary>
    private static bool IsOggOpus(string path)
    {
        Span<byte> head = stackalloc byte[512];
        using var file = File.OpenRead(path);
        var read = file.Read(head);
        return head[..read].IndexOf("OpusHead"u8) >= 0;
    }

    // Opus's canonical output rate; mono streams are upmixed by asking the
    // decoder itself for stereo.
    private const int OpusSampleRate = 48000;
    private const int OpusChannels = 2;

    // Hard stop for the up-front decode: past this the file is clearly not
    // an alert sound, and decoding on would stall the framework thread and
    // balloon memory (48 kHz stereo 16-bit PCM is ~11.5 MB per minute). The
    // Mentions tab's 5 s warning stays advice; this only guards the process.
    private const int MaxDecodedSeconds = 30;
    private const int MaxDecodedBytes = MaxDecodedSeconds * OpusSampleRate * OpusChannels * sizeof(short);

    /// <summary>
    /// Decodes the whole file to PCM up front — alert sounds are seconds
    /// long — so replays rewind a MemoryStream instead of re-decoding.
    /// Files running past <see cref="MaxDecodedSeconds"/> are rejected,
    /// which surfaces as the usual failed-play fallback.
    /// </summary>
    private static WaveStream DecodeOpus(string path)
    {
        using var file = File.OpenRead(path);

        // Stay on the managed decoder; probing for a native libopus inside
        // the game process buys nothing for a short alert sound.
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        var opus = new OpusOggReadStream(OpusCodecFactory.CreateDecoder(OpusSampleRate, OpusChannels), file);

        var pcm = new MemoryStream();
        while (opus.HasNextPacket)
        {
            if (pcm.Length > MaxDecodedBytes)
                throw new InvalidDataException($"Opus audio runs past the {MaxDecodedSeconds}s ceiling for alert sounds");

            var samples = opus.DecodeNextPacket();
            if (samples != null)
                pcm.Write(MemoryMarshal.AsBytes<short>(samples));
        }

        if (pcm.Length == 0)
            throw new InvalidDataException($"No Opus audio decoded: {opus.LastError ?? "empty stream"}");

        pcm.Position = 0;
        return new RawSourceWaveStream(pcm, new WaveFormat(OpusSampleRate, 16, OpusChannels));
    }

    private void DisposeAudio()
    {
        _output?.Dispose();
        _output = null;
        _volume = null;
        _reader?.Dispose();
        _reader = null;
        _loadedPath = null;
    }

    public void Dispose() => DisposeAudio();
}
