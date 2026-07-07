# Contributing to GobchatEx

Issues and PRs welcome. This guide covers the development setup and PR
checklist; the [README](README.md#contributing) covers the AI-disclosure
policy that applies to every PR.

## Prerequisites

- **.NET 10 SDK** — the test project targets `net10.0`.
- **XIVLauncher with a dev environment** — the plugin project uses
  `Dalamud.NET.Sdk/15.0.0`, which resolves `Dalamud.dll` from
  `%AppData%\XIVLauncher\addon\Hooks\dev`. Launch the game through
  XIVLauncher at least once so that folder exists.
- Unit tests need **neither** the game nor XIVLauncher — `dotnet test`
  compiles the Dalamud-free `GobchatEx/Core/` and `GobchatEx/Localization/`
  sources directly.
- **Chat 2's `local/dev-combined` fork build** — only if you're changing the
  Chat 2 styling integration (Milestone 3.5: `Chat/ChatTwoStyleProvider.cs`,
  the ChatTwoTab settings page, or the Debug page's "Chat 2 IPC" tab). Stock
  released Chat 2 doesn't have the styling IPC yet
  ([ChatTwo#186](https://github.com/Infiziert90/ChatTwo/issues/186)); every
  other change needs at most a stock Chat 2 install (or none at all).

## Commands

<!-- AUTO-GENERATED from GobchatEx/GobchatEx.csproj and tests/GobchatEx.Core.Tests/GobchatEx.Core.Tests.csproj -->
| Command | Description |
| ------- | ----------- |
| `dotnet build` | Build plugin + tests; plugin output in `GobchatEx/bin/Debug/` (Release also packs `bin/Release/GobchatExPlugin/latest.zip`) |
| `dotnet test` | Run the Core unit tests (xunit; no game install needed) |
| `dotnet test --collect "Code Coverage;Format=cobertura"` | Tests with coverage — use this built-in collector; coverlet cannot instrument net10.0 and silently reports 0 lines |
| `dotnet format` | Apply the `.editorconfig` style rules |
<!-- /AUTO-GENERATED -->

Loading and hot-reloading the built plugin in-game is described in
[README → Building locally](README.md#building-locally).

## Architecture constraint (test-enforced)

`GobchatEx/Core/` and `GobchatEx/Localization/` must stay Dalamud-free
([ADR 0002](adr/0002-pure-dalamud-free-parser-core.md)). The test project
compiles `Core/**/*.cs` and `Localization/**/*.cs` directly instead of
referencing the plugin project, so any Dalamud using-directive there breaks
`dotnet test`. Portable logic goes into Core (test-first); the Dalamud-facing
layers (`Chat/`, `Windows/`, `Plugin.cs`) stay thin.

## Testing

- Unit tests live in `tests/GobchatEx.Core.Tests` (xunit + FluentAssertions).
  Use the Arrange-Act-Assert pattern and name tests by behavior.
- The Dalamud-facing layer is validated manually: run the in-game smoke test
  in [README → Testing](README.md#testing) for any change touching `Chat/`,
  `Windows/`, or `Plugin.cs`.
- Debug builds add a Debug settings page (`#if DEBUG`, never in Release) with
  live exercisers for the range filter and the Chat 2 styling IPC — use it
  instead of writing throwaway test code for those two surfaces.

## Code style

Enforced via [.editorconfig](../.editorconfig) — common goatcorp plugin
conventions:

- 4-space indent for C# (2 for JSON/YAML/Markdown/XML-style files), LF line
  endings, UTF-8, final newline.
- File-scoped namespaces, using-directives outside the namespace, new-line
  braces, `var` preferred.

## PR checklist

- [ ] `dotnet build` succeeds and `dotnet test` is green
- [ ] New or changed Core logic comes with unit tests
- [ ] Manual in-game smoke test run if the Dalamud-facing layer changed
- [ ] Chat 2's `local/dev-combined` fork build tested against if the styling
      IPC integration changed (Milestone 3.5)
- [ ] `packages.lock.json` committed if dependencies changed (Plogon requirement)
- [ ] AI assistance beyond autocomplete disclosed per the
      [AI policy levels](README.md#contributing)
