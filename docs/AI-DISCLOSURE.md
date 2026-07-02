# AI Usage Disclosure

This file tracks AI involvement in GobchatEx's development, using the
disclosure levels from the [Dalamud AI policy](https://dalamud.dev/plugin-publishing/ai-policy):
**None/Hint**, **Assist**, **Pair**, **Copilot**, **Auto**. It exists so the
PR description for the eventual DalamudPluginsD17 submission can be written
accurately, since git commit attribution is disabled for this project and
nothing else in the repo records what was AI-assisted.

## Log

| Date | Scope | Tool | Level | Notes |
|---|---|---|---|---|
| 2026-07-01 | v1 project scaffold (`Plugin.cs`, `Configuration.cs`, `Windows/`, `.csproj`, manifest, README) | Claude Code (`dalamud-plugin-scaffold` skill) | Copilot | AI generated the full scaffold from a template; author reviewed the code, built it, and loaded it in-game before accepting. |

## Current disclosure statement

Ready to paste into the PR description for the next DalamudPluginsD17
submission — update it as new AI-assisted work is added to the log above.

> **AI usage disclosure:** The initial project scaffold (plugin skeleton,
> windows, manifest) was generated with Claude Code at the Copilot level
> (AI implements, human plans and reviews). All code was reviewed, built,
> and tested in-game by the author before acceptance.

Add a row to the log for any PR or work session involving meaningful AI
assistance beyond autocomplete, so this stays accurate at submission time.
