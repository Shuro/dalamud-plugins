# 2. Keep the segmentation engine Dalamud-free and test it via source linking

Date: 2026-07-01

## Status

Accepted

## Context

The delimiter parser is a port of the old GobchatEx `ReplaceTypeByToken`
state machine — pure string/span logic with real edge cases (unclosed
delimiters, delimiters spanning payload boundaries, mention overlap merging)
that deserve unit tests. Dalamud plugins are awkward to unit test: the
SDK-provided `Dalamud.dll` lives in the local XIVLauncher dev install
(`%AppData%\XIVLauncher\addon\Hooks\dev\`), which CI runners don't have, and
most Dalamud services cannot be constructed outside the game.

## Decision

All parsing/segmentation logic lives in `GobchatEx/Core/` and references no
Dalamud, Lumina or ImGui types — it operates on plain strings and produces
`SegmentSpan` lists. The test project (`tests/GobchatEx.Core.Tests`) is a
plain `Microsoft.NET.Sdk` project that compiles the Core sources directly via
`<Compile Include="..\..\GobchatEx\Core\**\*.cs" />` instead of referencing
the plugin project.

## Consequences

- `dotnet test` runs anywhere (CI included) without a Dalamud install.
- The boundary is enforced mechanically: an accidental Dalamud `using` in
  `Core/` breaks the test build immediately.
- Dalamud-facing layers (`Chat/ChatListener`, `Chat/PayloadRewriter`, UI) are
  kept deliberately thin and are validated manually in game.
- If Core is ever shared with another project (e.g. an overlay), it can be
  promoted to a real class library; DalamudPackager picks up extra DLLs
  automatically.
