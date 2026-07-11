# 3. Mention sound alerts use game sound effects only (defer custom audio files)

Date: 2026-07-01

## Status

Superseded by [4. Custom mention sound files via NAudio](0004-custom-sound-files-naudio.md)

## Context

ChatAlerts supports both the 16 built-in game chat sound effects
(`<se.1>`–`<se.16>`, played via FFXIVClientStructs
`UIGlobals.PlayChatSoundEffect`) and arbitrary audio files played through the
NAudio library. Custom files drag in the feature's only new NuGet dependency
plus a file picker, missing-file/device error handling, player lifecycle
management and a separate volume slider.

## Decision

Version 1 plays only the built-in game sound effects when a mention matches.
NAudio and custom audio files are deferred.

## Consequences

- Zero new package dependencies; the sound call is one
  FFXIVClientStructs invocation from the chat handler (framework thread).
- Volume follows the game's own sound-effects mixer — no plugin-side volume
  code, and the sounds are the same ones players already use in macros.
- `Chat/SoundPlayer` is the single seam to extend when custom files are
  added later (ChatAlerts' `AlertCache` is the ready-made model).
