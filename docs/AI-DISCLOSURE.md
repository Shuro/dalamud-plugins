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
| 2026-07-06 | Settings-window rework, commit `7038efb`: instant-apply (staged Save/Apply/Cancel model removed), green/red toggle switches, movable-window setting removed, range-filter UX (engine-limit markers, slider cap); plus codemap/doc sync | Claude Code | Copilot | AI implemented from a human-approved plan with an AI code-review pass; author directs, reviews, and smoke-tests in-game. |
| 2026-07-06 | Config split into per-feature JSON files, commits `689e5f3`/`0493d7f`: `GobchatEx/Config/` section classes + aggregate, per-file save/load with per-section defaults on missing/corrupt files, per-section settings-window change detection, v0–v5 migrations removed (plugin unpublished); plus codemap/doc sync | Claude Code | Copilot | AI implemented from a human-approved plan; author directs, reviews, and smoke-tests in-game. |
| 2026-07-07 | v0.8.0 public-distribution prep, commits `4552c7b`/`c34c2b6`: always-on Chat 2 range styling, General/Roleplay nav rail, `repo.json` custom plugin repository | Claude Code | Copilot | AI implemented from a human-approved plan; author directs, reviews, and smoke-tests in-game. |
| 2026-07-08/09 | Raw-RGBA color rework and follow-ups, commits `9417191`/`24c9795`/`5700e0a`: colors as raw SeString Color/EdgeColor macros instead of UIColor rows (full color picker replaces the sheet swatch picker), unmarked `/say`/`/em` text typed Say/Emote by channel, range fade keeping each channel's own color (incl. reading Chat 2's persisted channel colors); plus codemap/doc sync | Claude Code | Copilot | AI implemented from a human-approved plan; author directs, reviews, and smoke-tests in-game. |

## Current disclosure statement

Ready to paste into the PR description for the next DalamudPluginsD17
submission — update it as new AI-assisted work is added to the log above.

> **AI usage disclosure:** The initial project scaffold and later feature
> work recorded in the log above (most recently the raw-RGBA color rework)
> were implemented with Claude Code at the Copilot level (AI implements,
> human plans and reviews). All code was reviewed, built, and tested
> in-game by the author before acceptance.

Add a row to the log for any PR or work session involving meaningful AI
assistance beyond autocomplete, so this stays accurate at submission time.
