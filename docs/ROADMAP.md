# Roadmap

This plugin is the successor to the standalone
[GobchatEx](https://github.com/Shuro/GobchatEx) Windows app, which read FFXIV
chat from process memory and rendered a WebView2 chat overlay. As a Dalamud
plugin, chat arrives directly via `IChatGui` and highlighting is rewritten
into the native chat log ([ADR 0001](adr/0001-native-chat-sestring-rewriting.md)) —
so large parts of the app (overlay, memory reading, updater) are simply
unnecessary here. This document sequences the features that *are* worth
migrating.

Paths like `Core/Chat/PlayerMentionResolver.cs` below refer to the original
app's source (under `src/Gobchat.App/` in that repository); they mark where
the logic to port lives.

## Guiding decisions

- **No overlay window.** The chat is directly available in-game; highlighting
  happens in the native chat log (ADR 0001).
- **Chat logging comes last.** Useful, but not urgent.
- **Chat Tabs are dropped.** Tabs only make sense inside an overlay window,
  and the native chat log already has game-managed tabs.
- **Per-channel Colors are dropped.** The game's own settings (Character
  Configuration → Log Window Settings → Log Text Color) already cover
  per-channel colors; the plugin keeps only its RP segment, mention, and
  group colors.
- Portable logic is ported into the Dalamud-free `GobchatEx/Core/`
  ([ADR 0002](adr/0002-pure-dalamud-free-parser-core.md)) test-first;
  Dalamud-facing layers stay thin.

## Done — v1: RP highlighting

- Say / Emote / OOC segment recoloring in the native chat log via SeString
  payload rewriting
- Mention trigger words (case-insensitive whole words)
- Mention alerts using the game's built-in sound effects, with cooldown
  ([ADR 0003](adr/0003-game-sound-effects-only-v1.md))
- Settings window (nav rail, staged Save/Apply/Cancel)

## Milestone 1 — Advanced mentions (player-name matching) — Done

Match your character's name — not just a static word list. Ported from the
app's mention engine (pure logic, unit-tested upstream):

- Full / first / last name and partial (substring) matching, plus Miqo'te
  apostrophe-segment matching (`Core/Chat/PlayerMentionResolver.cs`)
- Fuzzy typo-tolerant matching with Conservative / Balanced / Aggressive
  levels (`Core/Util/StringSimilarity.cs`, OSA edit distance)
- NFKC unicode folding so decorative "fancy font" text still matches
  (`Core/Util/UnicodeNormalizer.cs`)
- Characters are added via an explicit "Add Current Character" button in the
  Mentions tab (backed by `IPlayerState`), not auto-learned silently on
  login — per-character match flags and custom words, effective-mention union
- Mentions tab grows a character list with per-character options

Complexity: medium. Extends the existing `MentionMatcher` / `MessageSegmenter`
seam in `GobchatEx/Core/`.

## Milestone 2 — Player groups — In progress

Sort players into colored groups so friends and RP partners stand out:

- Custom groups matched by player-name lists, plus the game's seven friend
  groups matched by sender glyph (★●▲◆♥♠♣) —
  `Core/Chat/ChatMessageTriggerGroupSetter.cs`; custom groups win over
  friend groups, first match wins
- Sender-name recoloring per group through the existing payload rewriter
- New Groups settings tab: group list, per-group color, player list editor
- Right-click a player in chat to add/remove them from a group
  (Dalamud `IContextMenu`)
- Commands: `/gobchat group <name> add|remove|clear <player> [world]`,
  `/gobchat group list` (port of the app's `/e gc group …`)

Complexity: medium-high (context-menu integration; sender payload surgery
around party/alliance prefixes).

## Milestone 3 — Range filter

Fade or hide chat from far-away players (great on crowded RP servers):

- Linear fade math ported from `Core/Chat/ChatMessageActorDataSetter.cs`
- Distances from Dalamud `IObjectTable` positions at message time (replaces
  the app's memory-reader actor manager)
- Native chat has no per-line opacity, so fading maps to darkened color
  steps; beyond the cutoff the message is suppressed
- Configurable cutoff/fade distances, channel scope (default Say/Emote),
  and "mentions ignore range"

Complexity: medium.

## Milestone 4 — Profiles

Config portability:

- Export/import the plugin configuration as JSON
- Optional: import a profile from the standalone GobchatEx app (mapping the
  still-relevant subset of its `default_profile.json` schema)
- Possibly multiple named profiles with `/gobchat profile load <name>`

Complexity: low-medium.

## Milestone 5 — Chat logging (last)

Write chat to disk, ported from the app's `Module/Misc/Chatlogger/`:

- Per-session `.log` files with a customizable format string
  (`{channel} [{date} {time-full}] {sender}: {message}`)
- Per-channel selection, optional per-character subfolders, new file per
  login, hardened path handling (`Core/Util/PathSecurityUtil.cs`)

Complexity: low-medium.

## Backlog / opportunistic

- Custom sound files for mention alerts (extend the `SoundPlayer` seam,
  ADR 0003)
- Localization (the app ships EN/DE strings)

## Explicitly not migrating

| App feature | Why not |
| --- | --- |
| Overlay / WebView2 / TypeScript UI | Native chat log is the render target (ADR 0001) |
| Chat Tabs | Needs an overlay; the game's chat log already has tabs |
| Per-channel Colors | Covered by the game's Log Text Color settings |
| Memory reading (Sharlayan), actor manager, process detection | Replaced by Dalamud services (`IChatGui`, `IObjectTable`, `IClientState`) |
| Updater, tray icon, first-run dialogs, OS-global hotkeys | Dalamud handles plugin lifecycle; window toggles replace hotkeys |
| Autotranslate resolver | Dalamud/Lumina resolves autotranslate payloads natively |
| Config upgraders | Only the profile-import subset (Milestone 4) is relevant |
| ACT-type logger, dry-run replay harness | Dead code / dev tooling in the app |

Each milestone gets its own detailed implementation plan before coding;
Core logic is ported test-first (`dotnet test`), and every milestone ends
with the manual in-game smoke test described in the
[README](README.md#testing).
