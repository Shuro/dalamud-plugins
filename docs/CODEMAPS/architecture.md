<!-- Generated: 2026-07-13 (v0.10.2, post emote autodetect + Chat 2 fade opacity + range preview rings) | Files scanned: 75 (+20 tests) | Token estimate: ~1900 -->

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
prefix match in          help         → command list to chat
Core/LegacyEchoCommand)  config open  → open+focus settings
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

## Localization

Localization/Loc.cs — ResourceManager over Resources/Language.resx (en) with
de satellite; missing key renders the key itself. Culture follows Dalamud's
UI language unless GeneralConfig.LanguageOverride is set; re-resolved via
`Plugin.RefreshLanguage()` after saves.

## Settings UI (Windows/)

- SettingsWindow.cs (433) — nav rail: General (GeneralTab, ChatLogTab) /
  Roleplay (FormattingTab, MentionsTab, GroupsTab, RangeTab, ChatTwoTab) /
  divider / Debug (`#if DEBUG`) / About. Native collapse enabled; title-bar
  Ko-fi heart ordered via `Priority` left of Dalamud's options button; a
  Ko-fi button in the footer bar. Instant-apply: each tab edits its live
  config section; a debounced per-section JSON-snapshot compare (Update tick
  + OnClose/Dispose flush) persists only the section files that changed and
  applies once — no Save/Apply/Cancel. Debug builds add live Chat 2
  connect/disconnect status on the footer's right.
- SettingsUi.cs (294) — shared tab widgets: section headers, warnings,
  green/red `ToggleSwitch`, `RgbaColorEdit` packed-RGBA swatch/picker,
  aligned slider rows, Ctrl+Shift-gated `DangerButton`.
- GeneralTab.cs (199) — language override, Quickbar toggle + attach-to-chat +
  hide-condition grid, legacy echo fallback, optional-plugin (Chat 2) status.
- ChatLogTab.cs (146) — start/stop button driving ChatLogger's runtime state
  directly (not config), live status line, folder picker
  (ImGuiFileDialog), per-character subfolders, channel grid.
- MentionsTab.cs (471) — trigger words, per-character matching (+ warning
  when the logged-in character isn't registered and active), own-message
  highlight suppression, fuzzy level, sound: game-effect picker or custom
  file (file picker + volume + preview through the same SoundPlayer
  pipeline).
- FormattingTab.cs (235) — segment colors, per-row reset, import from the
  game's own channel color (direct RGBA conversion), emote-autodetect
  toggles (Say/Party).
- GroupsTab.cs (349) / RangeTab.cs (154) / ChatTwoTab.cs (106) — group CRUD
  (rename, add current target) + Chat 2 background swatch; range sliders +
  in-game ring preview button + Chat 2 fade/hide toggles + start/end
  opacity sliders; per-Chat-2-tab suppress-flag table. Chat 2-only
  controls disabled with a hint while `IsConnected` is false.
- DebugTab.cs (442, `#if DEBUG`) — tab bar over `ChatTwoStyleIpcTester`,
  DebugRangePane.cs (274), DebugGroupsPane.cs (69), plus glow/color macro
  probes printed to the native log.

## Key Files

- GobchatEx/Chat/ChatListener.cs (608) — 3-pass rewrite subscription, config caches, channel-color resolution
- GobchatEx/Chat/ChatTwoStyleProvider.cs (472) — Chat 2 styling IPC producer + snapshot
- GobchatEx/Windows/QuickbarWindow.cs (311) — overlay bar, hide conditions, chat-window anchoring
- GobchatEx/Chat/ChatLogger.cs (280) — Dalamud shell of the chat logger: mapping, batching, folder resolution
- GobchatEx/Chat/SoundPlayer.cs (214) — game-effect + NAudio custom-file playback, cooldown
- GobchatEx/Chat/UiColorDimmer.cs (201) — fade-step dimming: RGBA multiply, UIColor-row remap, channel-color wrap
- GobchatEx/Core/MentionMatcher.cs (179) — compiled regexes + fuzzy tokens, interval merge
- GobchatEx/Core/MessageSegmenter.cs (162) — pipeline orchestration + mention overlay + emote autodetect + channel default type
- GobchatEx/Core/PlayerMentionResolver.cs (148) — name parts → whole/partial word lists
- GobchatEx/Chat/GroupCommandHandler.cs (145) — /gex group add|remove|list parsing + execution
- GobchatEx/Windows/RangeRingsOverlay.cs (142) — preview rings: WorldToScreen projection onto the background draw list
- GobchatEx/Chat/PayloadRewriter.cs (132) — span → raw color-macro payload translation (+ RewriteUniform)
- GobchatEx/Chat/PlayerCommandHandler.cs (118) — /gex player count|list|distance execution
- GobchatEx/Core/ChatLogSession.cs (115) — chat-log session state machine (pure, injected clock)
- GobchatEx/Core/SegmentParser.cs (112) — one TokenRule pass; ported from Gobchat
- GobchatEx/Chat/SenderDistance.cs (99) — object-table distance lookup + snapshot
- GobchatEx/Core/GroupMatcher.cs (90) — ordered first-match group resolution
- GobchatEx/Core/ChatLogFormatter.cs (82) — {token} template → log line
- GobchatEx/Core/Util/PathSecurityUtil.cs (75) — containment check for the log folder
- GobchatEx/Core/CommandRouter.cs (66) — pure /gex subcommand routing
- GobchatEx/Core/RangeFade.cs (57) — pure distance→visibility/fade-step math + Chat 2 opacity remap
- GobchatEx/Core/RangeRingMath.cs (49) — preview-ring geometry + fade timing (pure)

## Testing

tests/GobchatEx.Core.Tests (20 files, 294 tests) compiles `GobchatEx/Core/**/*.cs`
and `GobchatEx/Localization/**/*.cs` directly (no project reference) — any
Dalamud using-directive there breaks `dotnet test` (ADR 0002). Covers the
parser/mention/group/range engines (incl. RangeFade's opacity remap and
RangeRingMath's preview geometry), command routing (CommandRouter,
PlayerCommandVerbParser, LegacyEchoCommand), and the chat-log engine
(ChatLogSession with injected clock, ChatLogFormatter, ChatLogNaming,
PathSecurityUtil). Loc tests run against throwaway resx fixtures. The
Dalamud-facing layer (Chat 2 IPC, logger I/O, Quickbar) is validated by
manual in-game smoke test (docs/README.md).

## Design Records

docs/adr/: 0001 native-chat SeString rewriting · 0002 Dalamud-free parser core ·
0003 game sound effects only (v1) · 0004 custom sound files via NAudio.
Roadmap: docs/ROADMAP.md (M1–M3.5 + M5 chat logging done; M4 profiles waiting).
