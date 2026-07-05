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

## Commands

<!-- AUTO-GENERATED from GobchatEx/GobchatEx.csproj and tests/GobchatEx.Core.Tests/GobchatEx.Core.Tests.csproj -->
| Command | Description |
| ------- | ----------- |
| `dotnet build` | Build plugin + tests; produces the packed plugin folder `bin/x64/Debug/GobchatEx/` |
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
- [ ] `packages.lock.json` committed if dependencies changed (Plogon requirement)
- [ ] AI assistance beyond autocomplete disclosed per the
      [AI policy levels](README.md#contributing)
