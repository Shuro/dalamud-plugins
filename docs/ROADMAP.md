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
- **Chat 2 is an optional second render target.** Native-log rewriting stays
  the baseline; per-message backgrounds and opacity — which SeString cannot
  express — land in Chat 2 through its upstream styling IPC
  ([ChatTwo#186](https://github.com/Infiziert90/ChatTwo/issues/186)) without
  making Chat 2 a dependency.
- **Chat logging was pulled ahead of profiles.** Originally sequenced last as
  useful-but-not-urgent; shipped as Milestone 5 while profiles (Milestone 4)
  wait.
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
- Settings window (nav rail, instant-apply edits — no Save/Apply/Cancel)

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

## Milestone 2 — Player groups — Done

Sort players into colored groups so friends and RP partners stand out:

- Custom groups matched by player-name lists, plus the game's seven friend
  groups matched by sender glyph (★●▲◆♥♠♣) —
  `Core/Chat/ChatMessageTriggerGroupSetter.cs`; custom groups win over
  friend groups, first match wins
- Sender-name recoloring per group through the existing payload rewriter
- New Groups settings tab: group list, per-group color, player list editor
- Right-click a player in chat to add/remove them from a group
  (Dalamud `IContextMenu`)
- Commands: `/gex group <name> add|remove|clear <player> [world]`,
  `/gex group list` (port of the app's `/e gc group …`)

Complexity: medium-high (context-menu integration; sender payload surgery
around party/alliance prefixes).

Group *background* tint — the most-requested group visual — needs Chat 2's
styling IPC and lives in Milestone 3.5; the native log can't draw backgrounds.

## Milestone 3 — Range filter — Done

Fade or hide chat from far-away players (great on crowded RP servers):

- Linear fade math ported from `Core/Chat/ChatMessageActorDataSetter.cs`
- Distances from Dalamud `IObjectTable` positions at message time (replaces
  the app's memory-reader actor manager)
- Native chat has no per-line opacity, so fading maps to darkened color
  steps that keep each segment's hue (the plugin's own colors darken in
  place; the game's embedded link colors remap to the nearest darker
  UIColor row; plain text fades in the channel's own configured color);
  beyond the cutoff the message dims to the
  darkest step — never suppressed, because `PreventOriginal` marks the
  message handled and it would then also vanish from Chat 2's history and
  any event-fed chat logger
- Configurable cutoff/fade distances, channel scope (default Say/Emote),
  and "mentions ignore range"

Complexity: medium.

In Chat 2 the same filter gets true per-message alpha instead of darkened
color steps, and render-only hiding — Milestone 3.5. The darkened-step
dimming here stays permanently as the "lite variant": it is what renders
whenever Chat 2's styling isn't available.

## Milestone 3.5 — Chat 2 styling integration — Built, awaiting upstream

Backgrounds and per-line opacity can't be expressed in SeString, so the
render support was upstreamed into Chat 2 as a message styling IPC
([issue #186](https://github.com/Infiziert90/ChatTwo/issues/186); PRs
[#187](https://github.com/Infiziert90/ChatTwo/pull/187) checkerboard rows,
[#188](https://github.com/Infiziert90/ChatTwo/pull/188) styling IPC,
[#189](https://github.com/Infiziert90/ChatTwo/pull/189) per-tab policies).
This milestone builds the consumer side — developed against the
`local/dev-combined` fork build, live for users once the PRs ship in stock
Chat 2. Scoping decisions: group *backgrounds* are Chat 2-gated (settings
disabled with a hint while the styling IPC isn't connected; native
sender-name coloring keeps working regardless), and the native darkened
steps remain the range filter's permanent lite variant with Chat 2 adding
real transparency on top.

- Production style provider (`Chat/ChatTwoStyleProvider.cs`): registers via
  `ChatTwo.SetMessageStyleProvider`, version-gated through
  `ChatTwo.StyleVersion`, re-registers on `ChatTwo.Available`, exposes
  `IsConnected` for the settings UI. The gate is single-provider (last
  writer wins), so the Debug tester suspends the production provider while
  it is registered and resumes it on unregister.
- Per-message decision from the already-tested Core pieces: group background
  via `GroupMatcher` (per-group `ChatTwoBackground` color), range alpha from
  `RangeFade.CalculateVisibility`, honoring channel scope and "mentions
  ignore range" against the message text. All inputs live in an immutable
  snapshot rebuilt on settings changes.
- Threading: Chat 2 calls the provider on its message-processing thread;
  the `SenderDistance` lookup marshals to the framework thread, everything
  else reads the snapshot.
- Settings: per-group background color in the Groups tab (Chat 2 only), Chat 2
  fade/hide toggles in the Range tab, and a Chat 2 page with connection status
  plus per-tab allow/suppress switches fed by `ChatTwo.GetTabs`/`TabsChanged`
  and sent through `ChatTwo.SetTabStylePolicies` when edits commit.
- Cleanup — the only remaining item, blocked until upstream merges the PRs:
  retire the `local/dev-combined` Chat 2 build and the fork branches once the
  PRs are merged and released; decide whether the Debug tab stays as a
  diagnostics page or goes.

No hard dependency: without Chat 2 installed — or if the PRs stall (fork
fallback, see project notes) — everything degrades to the native-log behavior
of Milestones 2 and 3.

Complexity: medium.

## Milestone 4 — Profiles — Deferred

Skipped in favor of Milestone 5 for now; still planned. Config portability:

- Export/import the plugin configuration as JSON
- Optional: import a profile from the standalone GobchatEx app (mapping the
  still-relevant subset of its `default_profile.json` schema)
- Possibly multiple named profiles with `/gobchat profile load <name>`

Complexity: low-medium.

## Milestone 5 — Chat logging — Done

Write chat to disk, ported from the app's `Module/Misc/Chatlogger/`:

- Per-session `.log` files (`chatlog_{date}_{character}.log`), one per
  login/character switch, created lazily with the first loggable message —
  an idle session never leaves an empty stray file
- The app's token format engine ported and unit-tested
  (`Core/ChatLogFormatter.cs`) but writing a clean new format: no legacy
  `Chatlogger Id:` header lines, channel names from a stable plugin-owned map
  (`TellIncoming`, `Linkshell1` — not the app's `TellRecieve`/`LinkShell_1`),
  UTF-8 without BOM — Gobchat-Log-Browser compatibility is explicitly not a
  goal. The format string stays the app default
  (`{channel} [{date} {time-full}] {sender}: {message}`), hand-editable in
  chatlog.json; no editor UI yet
- Logs tab: start/stop button with live status (mirrored by the Quickbar's
  log button), per-channel selection (conversational defaults incl. Echo),
  per-character subfolder toggle (default on), default folder
  `{ConfigDirectory}\logs` with a folder picker for any custom location —
  logging is a session-scoped manual action: never persisted, never
  auto-started, always stopped at logout
- Hardened path handling ported (`Core/Util/PathSecurityUtil.cs`): a
  hand-edited relative path resolves inside the config directory or falls
  back to the default with a warning
- Batched framework-thread writes (1 s flush); messages suppressed by other
  plugins (IsHandled) are not logged — the log mirrors what the player sees

## Backlog / opportunistic

- Custom sound files for mention alerts — done (wav/mp3/ogg via NAudio with
  a per-file volume slider and game-effect fallback,
  [ADR 0004](adr/0004-custom-sound-files-naudio.md))
- Localization — done (the settings UI ships EN/DE strings via
  `GobchatEx/Localization/Loc.cs`, with resx fallback to English)

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
