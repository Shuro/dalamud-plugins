using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Concentus;
using Concentus.Oggfile;
using FFXIVClientStructs.FFXIV.Client.UI;
using GobchatEx.Config;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GobchatEx.Chat;

/// <summary>
/// Plays the mention and per-group alerts, each with its own cooldown timer: a built-in chat
/// sound effect (volume follows the game's own sound-effects mixer) or a custom audio file via
/// NAudio (own volume, ADR 0004). Must be called from the framework thread (chat handlers and
/// the config UI both are). Files are decoded lazily on first play and cached per path as raw
/// PCM — the mention sound plus any number of per-group sounds share one player (ADR 0005) — so
/// a settings edit costs one file read on the next alert; a failed file play logs, falls back to
/// the game effect and retries the file on the following alert.
/// <para>
/// Every play gets its own <see cref="WaveOutEvent"/> over its own stream on the shared PCM
/// buffer. Rewinding one shared reader and restarting one shared output raced: Stop() doesn't
/// join the playback thread, so an immediate Play() (0 s cooldown, preview spam) could run two
/// playback threads over the same stream. Now a new alert just stops the previous one, which
/// shares nothing with it.
/// </para>
/// </summary>
public sealed class SoundPlayer : IDisposable
{
    private long? _lastMentionPlayedMs;
    private long? _lastGroupPlayedMs;

    // Wholesale reset instead of LRU once the cap is hit: a real config holds a handful of
    // files (one mention sound + a few groups), so the cap only guards a pathological config,
    // and re-loading a few short alert files is cheap.
    private const int MaxCachedFiles = 8;
    private readonly Dictionary<string, DecodedAudio> _cache = new(StringComparer.OrdinalIgnoreCase);

    private Playback? _current;

    /// <summary>A file's whole decoded content; immutable, shared by every play of that file.</summary>
    private sealed record DecodedAudio(byte[] Pcm, WaveFormat Format);

    /// <summary>
    /// One play: its own stream and output device. Disposed exactly once — by its own
    /// PlaybackStopped (raised on NAudio's playback thread), by the next alert stopping it, or by
    /// the player's Dispose, whichever comes first.
    /// </summary>
    private sealed class Playback(WaveStream stream, WaveOutEvent output) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            output.Dispose();
            stream.Dispose();
        }
    }

    public void TryPlay(MentionsConfig config)
        => TryPlayAlert(ref _lastMentionPlayedMs, config.MentionSoundCooldownMs, config, "Mention");

    /// <summary>The per-group alert (Milestone 6). All groups share one cooldown timer —
    /// spam protection, not a per-group rhythm (ADR 0005).</summary>
    public void TryPlayGroup(IAlertSoundSettings sound, int cooldownMs)
        => TryPlayAlert(ref _lastGroupPlayedMs, cooldownMs, sound, "Group");

    private void TryPlayAlert(ref long? lastPlayedMs, int cooldownMs, IAlertSoundSettings sound, string kind)
    {
        var now = Environment.TickCount64;
        if (lastPlayedMs is { } last && now - last < cooldownMs)
        {
            Plugin.Log.Debug("{Kind} sound suppressed: cooldown ({RemainingMs} ms left)",
                kind, cooldownMs - (now - last));
            return;
        }

        lastPlayedMs = now;

        if (sound.SoundUseCustomFile && PlayFile(sound.SoundFilePath, sound.SoundVolume))
            return;

        Play(sound.SoundEffect);
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
            if (!_cache.TryGetValue(path, out var audio))
                audio = LoadFile(path);

            StopCurrent();
            _current = Start(audio, volume);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Playing alert sound file {Path} failed; the game sound effect plays instead", path);
            _cache.Remove(path);
            return false;
        }
    }

    /// <summary>Drops the cached decode for <paramref name="path"/> so the next play re-reads the
    /// file — the settings UI calls this when a file is (re)picked or previewed, since a file
    /// replaced on disk under the same path would otherwise keep playing the old audio.</summary>
    public void Invalidate(string path)
    {
        if (!string.IsNullOrEmpty(path))
            _cache.Remove(path);
    }

    private static Playback Start(DecodedAudio audio, float volume)
    {
        var stream = new RawSourceWaveStream(new MemoryStream(audio.Pcm, writable: false), audio.Format);
        WaveOutEvent? output = null;
        Playback? playback = null;
        try
        {
            // ToSampleProvider converts whatever the decoder emitted (float for vorbis, 16-bit
            // PCM for wav/mp3/opus) into the float samples the volume wrapper expects. Clamped
            // here rather than trusting the config: the UI caps at 100 %, but a hand-edited value
            // must not blast clipped audio.
            var volumeProvider = new VolumeSampleProvider(stream.ToSampleProvider())
            {
                Volume = Math.Clamp(volume, 0f, 1f),
            };
            output = new WaveOutEvent();
            output.Init(volumeProvider);

            var created = new Playback(stream, output);
            playback = created;
            output.PlaybackStopped += (_, _) => created.Dispose();
            output.Play();
            return created;
        }
        catch
        {
            // Init can fail at the device level (no output device, exclusive-mode conflict), by
            // which point the WaveOutEvent already holds an event handle nothing else would free.
            // Through the Playback once it exists: its dispose-once guard also covers a
            // PlaybackStopped that may already have fired.
            if (playback != null)
            {
                playback.Dispose();
            }
            else
            {
                output?.Dispose();
                stream.Dispose();
            }

            throw;
        }
    }

    private void StopCurrent()
    {
        _current?.Dispose();
        _current = null;
    }

    private DecodedAudio LoadFile(string path)
    {
        if (_cache.Count >= MaxCachedFiles)
            _cache.Clear();

        var audio = IsOgg(path) && IsOggOpus(path) ? DecodeOpus(path) : DecodeWithReader(path);
        _cache[path] = audio;
        return audio;
    }

    /// <summary>Reads a wav/aiff/mp3/vorbis file fully into memory in the reader's own output
    /// format, under the same <see cref="MaxDecodedSeconds"/> ceiling as opus.</summary>
    private static DecodedAudio DecodeWithReader(string path)
    {
        using var reader = CreateReader(path);
        var limit = (long)reader.WaveFormat.AverageBytesPerSecond * MaxDecodedSeconds;

        using var pcm = new MemoryStream();
        var buffer = new byte[Math.Max(reader.WaveFormat.BlockAlign, 1) * 4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcm.Write(buffer, 0, read);
            if (pcm.Length > limit)
                throw new InvalidDataException($"Audio runs past the {MaxDecodedSeconds}s ceiling for alert sounds");
        }

        if (pcm.Length == 0)
            throw new InvalidDataException("No audio decoded: empty stream");

        return new DecodedAudio(pcm.ToArray(), reader.WaveFormat);
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
        // An .ogg container holds either Vorbis or Opus (what Discord saves);
        // NVorbis only decodes the former, so pick by the codec marker on the
        // first Ogg page instead of trusting the extension. Windows' own ogg
        // codecs are an optional store package, hence two managed decoders.
        // Opus never reaches here — LoadFile and GetDuration route it to the Ogg-page paths.
        if (IsOgg(path))
            return new VorbisWaveReader(path);

        // Not AudioFileReader: it ships in the NAudio meta-package's glue
        // assembly, which drags in the WinForms SDK Plogon can't build against
        // (see GobchatEx.csproj). Pick the concrete reader by extension using
        // only NAudio.Core + NAudio.WinMM — wav/aiff read straight through, mp3
        // decodes through the WinMM ACM decompressor (runs on the player's
        // Windows box; managed no-op at build time).
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => new WaveFileReader(path),
            ".aiff" or ".aif" => new AiffFileReader(path),
            ".mp3" => new Mp3FileReaderBase(path, waveFormat => new AcmMp3FrameDecompressor(waveFormat)),
            var ext => throw new NotSupportedException($"Unsupported audio format '{ext}' for alert sound {path}"),
        };
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
    /// long — so replays read the cached PCM instead of re-decoding.
    /// Files running past <see cref="MaxDecodedSeconds"/> are rejected,
    /// which surfaces as the usual failed-play fallback.
    /// </summary>
    private static DecodedAudio DecodeOpus(string path)
    {
        using var file = File.OpenRead(path);

        // Stay on the managed decoder; probing for a native libopus inside
        // the game process buys nothing for a short alert sound.
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        var opus = new OpusOggReadStream(OpusCodecFactory.CreateDecoder(OpusSampleRate, OpusChannels), file);

        using var pcm = new MemoryStream();
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

        return new DecodedAudio(pcm.ToArray(), new WaveFormat(OpusSampleRate, 16, OpusChannels));
    }

    public void Dispose()
    {
        StopCurrent();
        _cache.Clear();
    }
}
