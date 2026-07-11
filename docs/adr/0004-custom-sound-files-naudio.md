# 4. Custom mention sound files via NAudio

Date: 2026-07-11

## Status

Accepted

Supersedes [3. Mention sound alerts use game sound effects only](0003-game-sound-effects-only-v1.md)

## Context

ADR 0003 shipped mention alerts with the 16 built-in game sound effects only
and deferred custom audio files, naming `Chat/SoundPlayer` as the seam to
extend and ChatAlerts' `AlertCache` as the ready-made model. Users want their
own alert sound — including ogg, the common format for community sound packs.
`NAudio.AudioFileReader` decodes wav/mp3 but not ogg, and Windows' own ogg
codecs are an optional store package, not something a plugin can rely on.
An `.ogg` container also holds either of two codecs: Vorbis, or Opus — what
Discord saves, and the first real-world file this feature was tested with.

## Decision

`SoundPlayer` gains an opt-in custom-file mode next to the game effects:

- **NAudio 2.2.1 + NAudio.Vorbis 1.5.0 + Concentus 2.2.2 /
  Concentus.Oggfile 1.0.7** (managed NVorbis and Opus decoders) — the
  plugin's first NuGet dependencies. Non-`.ogg` paths go to
  `AudioFileReader`; `.ogg` files are dispatched by the codec marker on the
  first Ogg page ("OpusHead" → Concentus, else NVorbis), since the extension
  alone can't tell Vorbis from Opus. Opus is decoded to PCM in full at load
  (alert sounds are seconds long; replay rewinds a MemoryStream). Every
  reader feeds one `VolumeSampleProvider` → `WaveOutEvent` chain carrying
  the file's own volume slider.
- **Lazy load keyed on path** instead of ChatAlerts' preload-on-config-change:
  the file is (re)loaded on the first play after the path changes. This keeps
  the player self-contained under the settings window's debounced
  instant-apply commits (the preview button can play a not-yet-committed
  path), at the cost of a few ms of file I/O on the framework thread once per
  path change.
- **Game-effect fallback**: a failed file play (missing file, decoder or
  device error) logs and plays the configured game effect instead — an alert
  is never silently lost. The Mentions tab warns when the path doesn't exist
  or the file runs longer than five seconds (duration probed without decoding
  — Opus reports it from the Ogg granule count).
- One `SoundPlayer` instance is owned by `Plugin` and shared by the chat
  handler and the Mentions tab's preview, so both go through the same NAudio
  pipeline and disposal.

## Consequences

- The packaged plugin now ships NAudio, NVorbis and Concentus assemblies
  (all managed, MIT-licensed; NAudio is already used by published plugins
  such as ChatAlerts, so this is a known-good path for DalamudPluginsD17).
- Custom-file volume is plugin-side (0–100 % of the file's native loudness)
  and independent of the game's mixers; game sound effects still follow the
  game's sound-effects volume.
- All NAudio calls stay wrapped in the `SoundPlayer` seam — nothing else in
  the codebase references NAudio types except the Mentions tab's preview
  call.
