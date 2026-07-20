<!-- Generated: 2026-07-20 (v1.0.0, post per-group alert sounds + mention history + changelog window + settings themes) | Files scanned: 79 (+24 tests) | Token estimate: ~2250 -->

# GobchatEx Roleplay Suite Architecture

Single-project Dalamud plugin (FFXIV via XIVLauncher). Native chat log,
recolored and filtered: RP segment recoloring (Say/Emote/OOC), mention
highlighting + sound (game effect or custom audio file), player-group
sender-name coloring, a range filter that fades/hides distant chat,
session-scoped chat logging to disk, and a Quickbar overlay for one-click
feature toggles. Chat 2 is an optional second render target (per-message
backgrounds + true opacity via its styling IPC) — nothing here requires it.
No server, no database, no network I/O.

## Layers

```text
GobchatEx/
├── Plugin.cs            entry point, [PluginService] injection, command
│                        registration, native context menu
├── Config/              Configuration aggregate + one section class per feature
│                        (GeneralConfig … ChatLogConfig), each persisted to its own
│                        JSON file (general.json … chatlog.json, per-file Version);
│                        element types SegmentStyle/CharacterMentionSettings/PlayerGroup
│                        — all colors stored as packed RGBA 0xRRGGBBAA (Core/RgbaColor),
│                        0 = "no color"; Serialize() shared by SaveSection() and the
│                        settings window's change detection
├── Core/                matching/math/parsing engine — Dalamud-FREE (ADR 0002,
│                        test-enforced): segments, mentions, groups, range math,
│                        command routing, chat-log session/format/naming, Util/
├── Chat/                Dalamud-facing: chat rewrite, groups, range, sound,
│                        chat logger, command handlers, Chat 2 IPC
├── Localization/        Loc.cs ResourceManager wrapper (also Dalamud-free)
├── Resources/           Language.resx + Language.de.resx (UI chrome only)
└── Windows/             ImGui UI (WindowSystem): SettingsWindow + QuickbarWindow;
                         RangeRingsOverlay (not a Window — background draw list);
                         SettingsTabs/ incl. #if DEBUG-only pages
```

## Chat Pipeline (per message, framework thread, native log)

Three independent `IChatGui.CheckMessageHandled` subscribers (fires after
every plugin's ChatMessage pass), deliberately not one: a failure in any
must not break the others. `Chat/ChatListener` (rewrite), `Chat/ChatLogger`
(disk log), `Chat/LegacyCommandListener` (legacy command fallback).

ChatListener runs three passes in `OnChatMessage`, in order — the fade step
is computed first and threaded into passes 1–2 (so their colors emit
pre-dimmed), then applied to everything else last:

```text
1. Body highlighting        gate: RpHighlightEnabled + channel set
   → Core/MessageSegmenter.Segment → SegmentParser (OOC>Emote>Say) → MentionMatcher
     channel default type: text left unmarked on /say → Say, /em → Emote
     (ChatListener.DefaultTypeFor, only while that style is enabled)
     emote autodetect: a quoted Say span flips remaining unmarked text to
     Emote — after mentions, before the channel default (DetectEmoteInSay
     default on / DetectEmoteInParty off, gated on the Emote style)
     own messages skip the mention overlay (SuppressHighlightFromSelf,
     default on; Echo exempt) — detection still runs for the sound decision
   → Chat/PayloadRewriter.Rewrite  spans → balanced raw Color/EdgeColor macro
     pairs (SeStringColorMacro, packed RGBA — not UIColor rows), pre-dimmed
     via UiColorDimmer.DimRgba when a fade step applies
   → SoundPlayer.TryPlay on mention (cooldown; own messages suppressed via
     Core/SelfSender) — game sound effect, or custom audio file (ADR 0004)

2. Sender group coloring    independent of the RP master switch/channels
   gate: not Tell/Echo/Error
   → Chat/SenderIdentity.Resolve → Chat/FriendGroupLookup → Core/GroupMatcher
     (rule order single-sourced in Chat/GroupRuleBuilder — custom groups
     first in config order, then the 7 friend groups)
   → Chat/PayloadRewriter.RewriteUniform → message.Sender recolored

3. Range fade (Milestone 3) gate: RangeFilterEnabled + channel set
   → Chat/SenderDistance.Resolve (IObjectTable positions) → Core/RangeFade.CalculateVisibility
   → mentions bypass (segment-without-rewrite probe) → Chat/UiColorDimmer.DimPayloads
     (plugin's own RGBA macros pass through pre-dimmed; game-embedded UIColor
     rows remap to the nearest darker sheet row; plain text wraps in the
     channel's own color — ResolveChannelColor — darkened the same way)
   Never suppresses (PreventOriginal would also hide the message from Chat 2's
   history/any logger) — beyond cutoff renders at the darkest step instead.
```

Derived state (segmenter, channel sets, style/group lookups, cached Chat 2
channel colors) is rebuilt only in `ChatListener.SettingsChanged()` (and on
Login/Logout), never per message.

## Chat Logging (Milestone 5)

Session-scoped disk logging: manual start/stop only (Quickbar button or
Logs tab), never auto-starts, forced off at logout, on/off not persisted.
`Chat/ChatLogger` (its own CheckMessageHandled subscriber) reads
OriginalSender/OriginalMessage (immune to other plugins' rewrites), skips
IsHandled-suppressed messages, and batches appends via Framework.Update
(1 s flush). All rules live Dalamud-free in Core (injected clock,
unit-tested):

- `Core/ChatLogSession` — state machine: one file per login/character
  switch, file named lazily on first flushed line (idle session ⇒ no empty
  file), lines dropped while logged out; emits `ChatLogWrite` instructions
- `Core/ChatLogFormatter` — token template ({channel}/{date}/{time-full}/
  {sender}/{message}, case-insensitive; unknown tokens render literally);
  format fixed to the app's default, hand-editable in chatlog.json only
- `Core/ChatLogNaming` — filesystem-safe character tokens + invariant
  minute-precision timestamps
- `Core/Util/PathSecurityUtil` — hand-edited relative folder paths must stay
  inside the plugin config dir (no `..` escape); invalid ⇒ logging disabled
- `Chat/ChatLogChannelNames` — stable archive names per channel (own table,
  not XivChatType.ToString())
- `Config/ChatLogConfig` — folder (no default; empty = logging disabled
  until the user picks one), per-character subfolders, channel selection
  (default: all conversational + Echo)

## Commands

`/gex` primary; `/gobchatex`, `/gobchat` aliases (three CommandInfo
handlers — Dalamud has no alias mechanism; DisplayOrder pins /gex first).
Parsing is Dalamud-free per ADR 0002; execution is thin Chat/ shells:

```text
Plugin.OnCommand ─┐
                  ├→ Chat/CommandDispatcher (resolves <t> via ITargetManager)
LegacyCommand  ───┘    → Core/CommandRouter.Parse
Listener                 empty        → toggle settings window
("/e gc …" echo          group|g …    → Chat/GroupCommandHandler
fallback, gated by       player|p …   → Chat/PlayerCommandHandler
GeneralConfig.Legacy-      (Core/PlayerCommandVerbParser: count / list /
EchoCommandFallback;        distance <name> [world] — via SenderDistance)
prefix match in          mention …    → Chat/MentionCommandHandler
Core/LegacyEchoCommand)    (Core/MentionCommandVerbParser: add/remove/list
                             <word> — mutates MentionsConfig.MentionTriggers,
                             same trim+case-insensitive dedupe as the tab)
                          log …        → Chat/LogCommandHandler
                            (Core/LogCommandVerbParser: start/stop/status —
                             same session-scoped ChatLogger calls as the
                             Logs tab/Quickbar buttons; no folder ⇒ error)
                         help         → command list to chat
                         config open  → open+focus settings
                         anything else→ "Unknown command" (retired app
                                        commands never silently no-op)
```

## Quickbar (Windows/QuickbarWindow.cs)

Compact movable overlay bar (hotbar-like): chat-log start/stop, one-click
toggles for the four features (RP highlight, mentions, groups, range),
settings + hide buttons. Visibility driven entirely by
`GeneralConfig.ShowQuickbar` (PreOpenCheck); per-condition hiding
(cutscene/gpose, logged out, UI hidden, loading, battle, chat hidden) in
DrawConditions, mirroring Chat 2's display options — plugin-wide Dalamud
UI-hide exemption flags set accordingly. Optional attach mode glues the bar
to the chat window's top edge each PreDraw: Chat 2's ImGui window when
present (`ImGuiP.FindWindowByName("###chat2")`), else the game's ChatLog
addon (IGameGui); no target ⇒ floats free, drag grip + NoMove restored.
Every click persists and applies immediately (no debounced commit).

## Colors (raw RGBA)

All plugin-applied colors are packed 0xRRGGBBAA uints rendered as raw SeString
Color (0x13) / EdgeColor (0x14) macros (`Chat/SeStringColorMacro` — envelope +
integer-expression encoding, push/pop pairs), bypassing the UIColor sheet.
Proven against vanilla chat and Chat 2's renderer via the Debug page's probes;
vanilla ignores the alpha byte, so production forces it opaque.
`Core/RgbaColor` converts Vector4↔packed and IGameConfig's 0xRRGGBB chat
colors → packed. The UIColor sheet only remains for dimming rows the *game*
embeds (links) and Debug swatches.

## Chat 2 Styling Integration (Milestone 3.5)

Parallel system, only active when Chat 2 (`ChatTwo`) is installed and
supports its message-styling IPC. Renders what SeString can't: per-group
message backgrounds and true per-message alpha (instead of the native
pass's darkened color steps).

- `Chat/ChatTwoStyleProvider.cs` — registers via `ChatTwo.SetMessageStyleProvider`
  gated on `ChatTwo.StyleVersion`; re-registers on `ChatTwo.Available` and on
  Dalamud's `ActivePluginsChanged` (Chat 2 disable has no IPC callback of its
  own). Exposes `IsConnected`/`KnownTabs` for the settings UI.
- Threading: Chat 2 calls `Evaluate` once per message on **its own thread**.
  All decision inputs live in one immutable `Snapshot`, rebuilt on the
  framework thread on construction/login/logout/`SettingsChanged`, swapped
  atomically; `SenderDistance.Snapshot()` refreshes on a framework-tick timer
  (250 ms) and is read lock-free. The provider thread never touches Dalamud
  services directly — blocking there risks deadlocking Chat 2's unload.
- Decision reuses the same Core pieces as the native passes: `GroupMatcher`
  over `GroupRuleBuilder` rules for `PlayerGroup.ChatTwoBackground`,
  `RangeFade.CalculateVisibility` remapped through `RangeFade.RemapOpacity`
  (start/end opacity curve, defaults 80→30 %) for alpha, same
  mentions-bypass segmenter, `SelfSender` for identity completion.
- Per-Chat-2-tab suppress flags (`TabsConfig.ChatTwoTabPolicies`) pushed
  via `ChatTwo.SetTabStylePolicies`; tab list from `GetTabs`/`TabsChanged`,
  pruned when a tab disappears.
- `#if DEBUG` only: `Chat/ChatTwoStyleIpcTester.cs` — manual IPC exerciser
  (register/unregister, ad-hoc rule, invocation log) behind the Debug page;
  suspends the production provider while it holds Chat 2's single-provider
  gate.
- No hard dependency: absent/unsupported Chat 2 just leaves `IsConnected`
  false; native-log behavior (passes 2–3 above) is unaffected.

## Range Filter (Milestone 3)

`Core/RangeFade.cs` — pure distance→visibility math (0–100, linear ramp
between fade-out and cut-off radii, ported from the app). Native log has no
per-line opacity, so partial visibility quantizes to one of six fade steps,
each a darkening multiplier (`UiColorDimmer.StepFactors`, 1.0→0.20; step 0
is a Debug-only identity reference). Per step, `Chat/UiColorDimmer`:
multiplies the plugin's own RGBA colors darker (`DimRgba`; PayloadRewriter
pre-dims before emitting macros), remaps game-embedded UIColor-row payloads
to the sheet row nearest the darkened color (`DimRow`, memoized), and wraps
text outside any color span in the channel's own chat color darkened the
same way. `ChatListener.ResolveChannelColor` resolves that channel color:
Chat 2's customized per-channel color when on file (`Chat/ChatTwoChannelColors`
reads ChatTwo.json straight off disk — no IPC, gated on
`InstalledPlugins`.IsLoaded, cached in `SettingsChanged`, degrades to empty
on any failure) → the player's vanilla Log Text Color (IGameConfig, per
channel) → grey fallback. `Chat/SenderDistance.cs` resolves a sender's
distance from `IObjectTable` positions (single lookup for the native pass
and /gex player distance; full snapshot for the Chat 2 provider's message
thread and /gex player count|list). Mentions optionally bypass the filter
entirely. Configurable per-channel scope, defaults Say/Emote/StandardEmote.

Chat 2's per-message alpha follows the app's fade curve instead of the raw
ramp: `RangeFade.RemapOpacity` lerps from RangeFilterStartOpacity (80 %, at
the fade-out distance) to RangeFilterEndOpacity (30 %, at the cut-off);
beyond the cut-off the render-only hide is unchanged.

Preview rings: the Range tab's preview button draws transient ground rings
around the character — yellow at fade-out, orange at cut-off — for ~8 s,
fading over the last second. Geometry/timing are Dalamud-free in
`Core/RangeRingMath` (ring points, fade alpha); `Windows/RangeRingsOverlay`
projects them via IGameGui.WorldToScreen and strokes the main viewport's
*background* draw list from Plugin.DrawUI (not a Window — rings sit behind
every plugin window). Zero radius renders as a dot at the character's feet;
radii are captured at click time.

## Mentions (Milestone 1)

Global trigger words union per-character resolved words for the logged-in,
remembered, active character (`IPlayerState.CharacterName`):

- Core/PlayerMentionResolver — full/first/last name (whole-word), partial
  substrings, Miqo'te apostrophe segments
- Core/MentionRuleBuilder — Config-free assembly: unions global triggers with
  one character's resolved words, allocates a per-word color/glow style id
  (0 = none), first-wins dedupe (global > character name > custom word)
- Core/StringSimilarity — OSA edit distance; FuzzyMatchLevel
  Conservative/Balanced/Aggressive budgets
- Core/UnicodeNormalizer — NFKC folding so decorative "fancy font" text matches

Each mention word may carry its own foreground/glow override (`MentionTrigger`
in `mentions.json`; one shared override per character for all its name-derived
matches). `SegmentSpan.StyleId` carries the match's style id through the
segmenter; `MentionMatcher`'s interval merge only merges same-style matches —
differently-styled overlaps split, with a deterministic priority (earlier
start, then longer match, then styled-over-default, then lower id) deciding
who wins the contested text. `Core/MentionStyleResolver` resolves an id to
colors, falling back per-component to `FormattingConfig.MentionStyle`; an
empty style table (Mention style disabled) degrades every override to plain.

`ChatListener.BuildMentionRules` is `internal static` so the Chat 2 provider
builds an identical mention-bypass segmenter from the same rules; it now just
maps `MentionsConfig`/`IPlayerState` onto `MentionRuleBuilder.Build`.

Alert sound (`Chat/SoundPlayer`, cooldown, framework thread): built-in chat
sound effect (game's own SFX mixer) or a custom audio file via NAudio with
its own volume (ADR 0004; wav/mp3 via AudioFileReader, ogg/vorbis via
NAudio.Vorbis, ogg/opus via Concentus). File loaded lazily, cached until
the path changes; a failed file play logs, falls back to the game effect,
retries next alert. Own messages never alert (`Core/SelfSender`) and by
default aren't highlighted either (SuppressHighlightFromSelf; Echo exempt
as the designated mention test channel); every skipped mention sound
(disabled/self/cooldown) logs a Debug reason.

## Player Groups (Milestone 2)

Custom groups (user-created, member = name + optional world) plus the
game's 7 fixed friend-list display groups (FfGroup 0=Star..6=Club). Sender
recoloring is native-log only; `PlayerGroup.ChatTwoBackground` (Chat 2-only,
disabled in the UI until connected) adds a per-message background there.
`Chat/GroupRuleBuilder` single-sources rule precedence for both render
targets (custom groups in config order, then friend groups by FfGroup).

- `Chat/FriendGroupLookup.cs` — snapshots `InfoProxyFriendList` (no Dalamud
  service wraps it) into a (name, world)→index map. Refreshed on login, once
  at plugin load if already logged in, live via `FriendListAddonListener`,
  and — Debug builds only — a manual button (`DebugGroupsPane`).
- `Chat/FriendListAddonListener.cs` — `IAddonLifecycle` listener on the
  "FriendList" addon's `PostRequestedUpdate` event; the only signal that
  reliably fires exactly when the player edits a friend's display group
  in-game (alternatives tried and rejected — see its doc comment).
- Membership entry points, all through `Chat/GroupMembershipActions`: native
  right-click menu (`Plugin.OnMenuOpened`), Chat 2's own context menu
  (`ChatTwoContextMenuIntegration`), `/gex group ...` (`GroupCommandHandler`),
  Settings → Groups tab (incl. add-current-target via ITargetManager).

### Per-group alert sounds (Milestone 6, ADR 0005)

Optional "ding when a group member speaks", the same game-effect/custom-file
quartet as mentions (`Config/IAlertSoundSettings`, implemented directly by
`PlayerGroup`; `MentionsConfig` maps its historical `MentionSound*` JSON
names onto the same interface). One shared editor (`AlertSoundEditor`) draws
both the Mentions tab's and the Groups tab's sound block.

- At most one sound per message — **the mention alert wins** on overlap
  (`ApplySenderGroupColor(message, fadeStep, mentioned)` receives whether the
  body pass already matched a mention); with the mention sound itself
  disabled, group sounds still play on mention messages.
- One shared cooldown across all groups (`GroupsConfig.GroupSoundCooldownMs`,
  default 5s) — spam protection, not a per-group rhythm — separate from the
  mention cooldown; `SoundPlayer.TryPlayGroup`.
- Own messages never play a group sound, no opt-out (unlike mentions'
  `SuppressSoundFromSelf`).
- Channel scope is `ChatListener.GroupingChannels` — an allow-list of
  conversational channels, *not* the same set the sender-recolor pass uses
  (ADR 0005's 2026-07-16 amendment: the original denylist let senderless
  system messages reach `GroupMatcher` via a degenerate identity and fire
  sounds on lines nobody sent).
- `Chat/SoundPlayer` generalized its single cached file to an 8-entry
  path-keyed cache (wholesale reset, not LRU) shared by mention and group
  alerts, each with its own cooldown timer.
- Friend groups are engine-supported (any `PlayerGroup`) but have no sound
  row in the Groups tab yet; hand-edited `groups.json` fields do work.

## Mention History (Milestone 7)

`Chat/MentionHistory` — in-memory ring buffer (50 entries, oldest evicted),
written whenever the body pass detects a mention regardless of the sound
settings. Nothing persisted (opt-in disk logging stays exclusive to
`ChatLogger`). Each `MentionHistoryEntry` snapshots plain strings at message
time (the message object is pooled) plus the mention `SegmentSpan`s and a
parallel `SpanColors` list — a span's per-word override foreground, or 0 to
fall back to the *live* default mention color at draw time (so re-coloring
the default retroactively recolors unstyled history). A monotonic
`Sequence` (not the list index) keys each row's ImGui id, so eviction can't
silently re-bind an open sender-context popup to a different player mid-click.
`Windows/MentionHistoryWindow.cs` — newest-first table (Time/Channel/
Sender/Matches/Message), hover tooltip for the full message with per-span
coloring (`SettingsUi.HighlightedTextWrapped`), right-click sender for the
same group add/remove actions as the chat context menu
(`GroupMembershipActions`). Toggled from the Quickbar; cleared with the plugin.

## Changelog

`Windows/ChangelogWindow.cs` — "what's new" popup ported from OtterGui's
Changelog widget. Auto-opens via `PreOpenCheck` when
`GeneralConfig.ChangelogLastSeenVersion` (an index into the hand-authored
`SeedEntries()` list, not a plugin version string) is behind the entry count,
gated by `ChangelogDisplayType` (New / HighlightOnly / Never). A fresh
install or a pre-changelog config both start already caught up
(`FreshInstallVersion = int.MaxValue`) so upgrading never dumps the whole
backlog on existing users. Persists its own two `general.json` fields
immediately (`Configuration.SaveSection`, not SettingsWindow's debounced
commit) so the watermark survives even if settings is never opened; tells
SettingsWindow to rebaseline afterward so its own dirty-check doesn't redo
the write. AboutTab's "View Changelog" button force-opens it
(`ChangelogWindow.ForceOpen`) regardless of the watermark.

## Localization

Localization/Loc.cs — ResourceManager over Resources/Language.resx (en) with
de satellite; missing key renders the key itself. Culture follows Dalamud's
UI language unless GeneralConfig.LanguageOverride is set; re-resolved via
`Plugin.RefreshLanguage()` after saves.

## Settings UI (Windows/)

- SettingsWindow.cs (499) — nav rail: General (GeneralTab, ChatLogTab) /
  Roleplay (FormattingTab, MentionsTab, GroupsTab, RangeTab, ChatTwoTab) /
  divider / Debug (`#if DEBUG`) / About. Native collapse enabled; title-bar
  Ko-fi heart ordered via `Priority` left of Dalamud's options button; a
  Ko-fi button in the footer bar. Instant-apply: each tab edits its live
  config section; a debounced per-section JSON-snapshot compare (Update tick
  + OnClose/Dispose flush) persists only the section files that changed and
  applies once — no Save/Apply/Cancel. `RequestRebaseline()` lets an
  external writer (ChangelogWindow) resync that snapshot without a spurious
  re-save. Applies the active `SettingsWindowTheme`'s frame colors in
  `PreDraw`/`Draw`; Text/Surface/DisabledText are pushed only inside `Draw`'s
  content scope so they don't recolor the title bar. Debug builds add live
  Chat 2 connect/disconnect status on the footer's right.
- SettingsWindowTheme.cs (153) — Guid-keyed registry of selectable window
  color schemes (`GeneralConfig.WindowThemeId`; unknown/removed ids fall
  back to `Guid.Empty` = "Dalamud Theme", no overrides). Each theme sets
  `FrameColors` (WindowBg/TitleBg/TitleBgActive/TitleBgCollapsed) and
  optionally a pastel `SettingsUi.TogglePalette` plus Text/Surface/
  DisabledText overrides for readability on light backgrounds. Picked via a
  combo in GeneralTab.
- SettingsUi.cs (429) — shared tab widgets: section headers, warnings,
  green/red `ToggleSwitch` (theme-aware palette), `RgbaColorEdit` packed-RGBA
  swatch/picker, aligned slider rows, Ctrl+Shift-gated `DangerButton`,
  `HighlightedTextWrapped` (per-span colored text, used by MentionsTab's
  tester and MentionHistoryWindow's tooltip).
- GeneralTab.cs (225) — language override, window theme combo, Quickbar
  toggle + attach-to-chat + hide-condition grid, legacy echo fallback,
  optional-plugin (Chat 2) status.
- ChatLogTab.cs (146) — start/stop button driving ChatLogger's runtime state
  directly (not config), live status line, folder picker
  (ImGuiFileDialog), per-character subfolders, channel grid.
- AlertSoundEditor.cs (200) — shared game-effect/custom-file sound editor
  (source radio, effect combo with instant preview, file path + browse +
  preview with missing/failed/too-long warnings) plus
  `DrawCooldownVolumeRow`, drawn against any `IAlertSoundSettings`. Extracted
  from the Mentions tab so the Groups tab's per-group sounds (Milestone 6)
  reuse it instead of duplicating; path-exists/duration probes cached per
  distinct path, capped at 64 with wholesale reset.
- MentionsTab.cs (526) — trigger words with per-word foreground/glow
  override, per-character matching (+ warning when the logged-in character
  isn't registered and active) via a 2-column toggle-switch grid pairing
  each name part with its partial/special variant, own-message highlight
  suppression, fuzzy level, a "try a message" tester rendering matches in
  their resolved colors, sound via `AlertSoundEditor`.
- FormattingTab.cs (236) — segment colors, per-row reset, import from the
  game's own channel color (direct RGBA conversion), emote-autodetect
  toggles (Say/Party).
- GroupsTab.cs (391) — group CRUD (rename, add current target), Chat 2
  background swatch, per-group sound row via `AlertSoundEditor` (Milestone
  6, shared cooldown slider inline with each enabled group's volume).
- RangeTab.cs (154) / ChatTwoTab.cs (106) — range sliders + in-game ring
  preview button + Chat 2 fade/hide toggles + start/end opacity sliders;
  per-Chat-2-tab suppress-flag table. Chat 2-only controls disabled with a
  hint while `IsConnected` is false.
- DebugTab.cs (442, `#if DEBUG`) — tab bar over `ChatTwoStyleIpcTester`,
  DebugRangePane.cs (274), DebugGroupsPane.cs (69), plus glow/color macro
  probes printed to the native log.

## Key Files

- GobchatEx/Chat/ChatListener.cs (728) — 3-pass rewrite subscription, config caches, channel-color resolution, group-sound gating
- GobchatEx/Windows/SettingsTabs/MentionsTab.cs (526) — trigger words, per-word color, per-character grid, tester
- GobchatEx/Windows/SettingsWindow.cs (499) — nav rail, debounced per-section commit, theme application
- GobchatEx/Chat/ChatTwoStyleProvider.cs (472) — Chat 2 styling IPC producer + snapshot
- GobchatEx/Windows/SettingsTabs/DebugTab.cs (442, `#if DEBUG`) — debug page tab bar
- GobchatEx/Windows/SettingsUi.cs (429) — shared tab widgets, per-span text highlighting
- GobchatEx/Windows/SettingsTabs/GroupsTab.cs (391) — group CRUD + per-group sound row
- GobchatEx/Windows/QuickbarWindow.cs (316) — overlay bar, hide conditions, chat-window anchoring
- GobchatEx/Chat/ChatLogger.cs (280) — Dalamud shell of the chat logger: mapping, batching, folder resolution
- GobchatEx/Core/MentionMatcher.cs (267) — compiled regexes + fuzzy tokens, same-style interval merge
- GobchatEx/Chat/SoundPlayer.cs (256) — game-effect + NAudio custom-file playback, path-keyed cache, per-source cooldown
- GobchatEx/Windows/ChangelogWindow.cs (242) — "what's new" popup, seen-watermark persistence
- GobchatEx/Windows/SettingsTabs/FormattingTab.cs (236) — segment colors, emote-autodetect toggles
- GobchatEx/Windows/SettingsTabs/GeneralTab.cs (225) — language, window theme, Quickbar options
- GobchatEx/Chat/UiColorDimmer.cs (201) — fade-step dimming: RGBA multiply, UIColor-row remap, channel-color wrap
- GobchatEx/Windows/SettingsTabs/AlertSoundEditor.cs (200) — shared sound-settings editor (mentions + groups)
- GobchatEx/Core/MessageSegmenter.cs (164) — pipeline orchestration + mention overlay + emote autodetect + channel default type
- GobchatEx/Windows/MentionHistoryWindow.cs (160) — recent-mentions table, per-span coloring, group context menu
- GobchatEx/Windows/SettingsWindowTheme.cs (153) — Guid-keyed window color-scheme registry
- GobchatEx/Chat/GroupCommandHandler.cs (145) — /gex group add|remove|list parsing + execution
- GobchatEx/Core/MentionRuleBuilder.cs (143) — style-id allocation, first-wins dedupe
- GobchatEx/Windows/RangeRingsOverlay.cs (142) — preview rings: WorldToScreen projection onto the background draw list
- GobchatEx/Chat/PayloadRewriter.cs (144) — span → raw color-macro payload translation (+ RewriteUniform)
- GobchatEx/Core/PlayerMentionResolver.cs (128) — name parts → whole/partial word lists
- GobchatEx/Chat/PlayerCommandHandler.cs (118) — /gex player count|list|distance execution
- GobchatEx/Core/ChatLogSession.cs (115) — chat-log session state machine (pure, injected clock)
- GobchatEx/Chat/MentionCommandHandler.cs (102) — /gex mention add|remove|list execution
- GobchatEx/Core/GroupMatcher.cs (90) — ordered first-match group resolution
- GobchatEx/Core/Util/PathSecurityUtil.cs (75) — containment check for the log folder
- GobchatEx/Core/RangeFade.cs (57) — pure distance→visibility/fade-step math + Chat 2 opacity remap
- GobchatEx/Core/RangeRingMath.cs (49) — preview-ring geometry + fade timing (pure)

## Testing

tests/GobchatEx.Core.Tests (24 files, 344 tests) compiles `GobchatEx/Core/**/*.cs`
and `GobchatEx/Localization/**/*.cs` directly (no project reference) — any
Dalamud using-directive there breaks `dotnet test` (ADR 0002). Covers the
parser/mention/group/range engines (incl. MentionRuleBuilder's style-id
allocation, MentionStyleResolver's per-component fallback, RangeFade's
opacity remap and RangeRingMath's preview geometry), command routing
(CommandRouter, PlayerCommandVerbParser, LogCommandVerbParser,
MentionCommandVerbParser, LegacyEchoCommand), and the chat-log engine
(ChatLogSession with injected clock, ChatLogFormatter, ChatLogNaming,
PathSecurityUtil). Loc tests run against throwaway resx fixtures. The
Dalamud-facing layer (Chat 2 IPC, logger I/O, Quickbar, per-group sound
policy) is validated by manual in-game smoke test (docs/README.md) — ADR
0005 notes group-sound policy has no unit tests by design (it lives in
`Chat/`, not `Core/`).

## Design Records

docs/adr/: 0001 native-chat SeString rewriting · 0002 Dalamud-free parser core ·
0003 game sound effects only (v1) · 0004 custom sound files via NAudio ·
0005 per-group alert sounds (+ 2× amendment: channel-scope allow-list fix,
shared-cooldown UI placement).
Roadmap: docs/ROADMAP.md (M1–M3.5 and M5 chat logging done; M6 per-group
sounds and M7 mention history built, awaiting in-game smoke test; M4
profiles waiting).
