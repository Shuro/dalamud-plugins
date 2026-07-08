<!-- Generated: 2026-07-09 (post raw-RGBA color rework) | Files scanned: 56 | Token estimate: ~1500 -->

# GobchatEx Roleplay Suite Architecture

Single-project Dalamud plugin (FFXIV via XIVLauncher). Native chat log,
recolored and filtered: RP segment recoloring (Say/Emote/OOC), mention
highlighting + sound, player-group sender-name coloring, and a range filter
that fades/hides distant chat. Chat 2 is an optional second render target
(per-message backgrounds + true opacity via its styling IPC) — nothing here
requires it. No server, no database, no network I/O.

## Layers

```text
GobchatEx/
├── Plugin.cs            entry point, [PluginService] injection, commands,
│                        native context menu
├── Config/              Configuration aggregate + one section class per feature
│                        (GeneralConfig … TabsConfig), each persisted to its own
│                        JSON file (general.json … tabs.json, per-file Version);
│                        element types SegmentStyle/CharacterMentionSettings/PlayerGroup
│                        — all colors stored as packed RGBA 0xRRGGBBAA (Core/RgbaColor),
│                        0 = "no color"; Serialize() shared by SaveSection() and the
│                        settings window's change detection
├── Core/                matching/math engine — Dalamud-FREE (ADR 0002, test-enforced)
├── Chat/                Dalamud-facing: chat rewrite, groups, range, sound, Chat 2 IPC
├── Localization/        Loc.cs ResourceManager wrapper (also Dalamud-free)
├── Resources/           Language.resx + Language.de.resx (UI chrome only)
└── Windows/             ImGui settings UI (WindowSystem); SettingsTabs/ incl. #if DEBUG-only pages
```

## Chat Pipeline (per message, framework thread, native log)

Subscribed on `IChatGui.CheckMessageHandled` (fires after every plugin's
ChatMessage pass). Three passes in `Chat/ChatListener.OnChatMessage`, in
order — the fade step is computed first and threaded into passes 1–2 (so
their colors emit pre-dimmed), then applied to everything else last:

```text
1. Body highlighting        gate: RpHighlightEnabled + channel set
   → Core/MessageSegmenter.Segment → SegmentParser (OOC>Emote>Say) → MentionMatcher
     channel default type: text left unmarked on /say → Say, /em → Emote
     (ChatListener.DefaultTypeFor, only while that style is enabled)
   → Chat/PayloadRewriter.Rewrite  spans → balanced raw Color/EdgeColor macro
     pairs (SeStringColorMacro, packed RGBA — not UIColor rows), pre-dimmed
     via UiColorDimmer.DimRgba when a fade step applies
   → SoundPlayer.TryPlay on mention (cooldown)

2. Sender group coloring    independent of the RP master switch/channels
   gate: not Tell/Echo/Error
   → Chat/SenderIdentity.Resolve → Chat/FriendGroupLookup → Core/GroupMatcher
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
  for `PlayerGroup.ChatTwoBackground`, `RangeFade.CalculateVisibility` for
  alpha, same mentions-bypass segmenter.
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
is a Debug-only identity reference — `ChatListener.FadeStepColors` rows now
only back the Debug page's swatches). Per step, `Chat/UiColorDimmer`:
multiplies the plugin's own RGBA colors darker (`DimRgba`; PayloadRewriter
pre-dims before emitting macros), remaps game-embedded UIColor-row payloads
to the sheet row nearest the darkened color (`DimRow`, memoized), and wraps
text outside any color span in the channel's own chat color darkened the
same way — a fading Yell stays yellowish instead of collapsing to shared
grey. `ChatListener.ResolveChannelColor` resolves that channel color:
Chat 2's customized per-channel color when on file (`Chat/ChatTwoChannelColors`
reads ChatTwo.json straight off disk — no IPC, gated on
`InstalledPlugins`.IsLoaded, cached in `SettingsChanged`, degrades to empty
on any failure) → the player's vanilla Log Text Color (IGameConfig, per
channel: Say/CustomEmote/StandardEmote/Yell/Shout) → grey fallback.
`Chat/SenderDistance.cs` resolves a sender's distance from `IObjectTable`
positions (both single-lookup, for the native pass, and a full snapshot for
the Chat 2 provider's message thread). Mentions optionally bypass the filter
entirely (a far-away mention still shows). Configurable per-channel scope,
defaults Say/Emote/StandardEmote.

## Mentions (Milestone 1)

Global trigger words union per-character resolved words for the logged-in,
remembered, active character (`IPlayerState.CharacterName`):

- Core/PlayerMentionResolver — full/first/last name (whole-word), partial
  substrings, Miqo'te apostrophe segments, custom words
- Core/StringSimilarity — OSA edit distance; FuzzyMatchLevel
  Conservative/Balanced/Aggressive budgets
- Core/UnicodeNormalizer — NFKC folding so decorative "fancy font" text matches

`ChatListener.BuildMentionRules` is `internal static` so the Chat 2 provider
builds an identical mention-bypass segmenter from the same rules.

## Player Groups (Milestone 2)

Custom groups (user-created, member = name + optional world) plus the
game's 7 fixed friend-list display groups (FfGroup 0=Star..6=Club). Sender
recoloring is native-log only; `PlayerGroup.ChatTwoBackground` (Chat 2-only,
disabled in the UI until connected) adds a per-message background there.

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
  (`ChatTwoContextMenuIntegration`), `/gobchat group ...` (`GroupCommandHandler`),
  Settings → Groups tab.

## Commands

`/gobchat`, `/gobchatex`, `/gex` (hidden) → settings window (the only
window; OpenConfigUi and OpenMainUi too). First arg `group`/`g` routes to
GroupCommandHandler instead.

## Localization

Localization/Loc.cs — ResourceManager over Resources/Language.resx (en) with
de satellite; missing key renders the key itself. Culture follows Dalamud's
UI language unless GeneralConfig.LanguageOverride is set; re-resolved via
`Plugin.RefreshLanguage()` after saves.

## Settings UI (Windows/)

- SettingsWindow.cs (359) — nav rail: General (GeneralTab, Logs placeholder) /
  Roleplay (FormattingTab, MentionsTab, GroupsTab, RangeTab, ChatTwoTab) /
  divider / Debug (`#if DEBUG`) / About. Native collapse enabled; title-bar Ko-fi button
  ordered via `Priority` to sit left of Dalamud's own options button.
  Instant-apply: each tab edits its live config section; a debounced
  per-section JSON-snapshot compare (Update tick + OnClose/Dispose flush)
  persists only the section files that changed and applies once —
  no Save/Apply/Cancel. Debug builds show live Chat 2 connect/disconnect status.
- SettingsUi.cs (176) — shared tab widgets: section headers, warnings,
  green/red `ToggleSwitch` (custom-drawn; Dalamud's ToggleButton hardcodes
  gray), `RgbaColorEdit` packed-RGBA swatch/picker (replaced the old
  UIColor-sheet picker window), Ctrl+Shift-gated `DangerButton`.
- FormattingTab.cs (186) — segment colors gain per-row reset-to-default and
  (Say/Emote only) "import from the game's own channel color" buttons —
  now a direct RGBA conversion (`RgbaColor.FromGameConfigColor`), no
  nearest-row matching.
- GroupsTab.cs (236) / RangeTab.cs (114) / ChatTwoTab.cs (86) — group CRUD
  plus Chat 2 background swatch; range distance sliders plus Chat 2 fade/hide
  toggles; per-Chat-2-tab suppress-flag table. All three disable Chat
  2-only controls with a hint while `ChatTwoStyleProvider.IsConnected` is false.
- DebugTab.cs (379, `#if DEBUG`) — tab bar over `ChatTwoStyleIpcTester`,
  DebugRangePane.cs (235: distance simulator, live nearby-player table, test
  message injection, per-channel color-source labels with live Chat 2 re-read,
  dimming-step injection), DebugGroupsPane.cs (55: live FriendGroupLookup dump),
  plus glow/color macro probes printed to the native log.

## Key Files

- GobchatEx/Chat/ChatListener.cs (431) — 3-pass event subscription, config caches, channel-color resolution
- GobchatEx/Chat/ChatTwoStyleProvider.cs (381) — Chat 2 styling IPC producer + snapshot
- GobchatEx/Chat/ChatTwoStyleIpcTester.cs (222, DEBUG) — manual IPC exerciser
- GobchatEx/Chat/UiColorDimmer.cs (168) — fade-step dimming: RGBA multiply, UIColor-row remap, channel-color wrap
- GobchatEx/Core/MentionMatcher.cs (158) — compiled regexes + fuzzy tokens, interval merge
- GobchatEx/Core/PlayerMentionResolver.cs (131) — name parts → whole/partial word lists
- GobchatEx/Core/MessageSegmenter.cs (126) — pipeline orchestration + mention overlay + channel default type
- GobchatEx/Chat/PayloadRewriter.cs (120) — span → raw color-macro payload translation (+ RewriteUniform)
- GobchatEx/Core/SegmentParser.cs (99) — one TokenRule pass; ported from Gobchat
- GobchatEx/Chat/SenderDistance.cs (82) — object-table distance lookup + snapshot
- GobchatEx/Chat/ChatTwoChannelColors.cs (81) — Chat 2 per-channel colors off its config file (no IPC)
- GobchatEx/Core/GroupMatcher.cs (75) — ordered first-match group resolution
- GobchatEx/Chat/SeStringColorMacro.cs (60) — raw Color/EdgeColor (0x13/0x14) macro envelopes
- GobchatEx/Core/RangeFade.cs (37) — pure distance→visibility/fade-step math
- GobchatEx/Core/RgbaColor.cs (32) — 0xRRGGBBAA packing + GameConfig conversion

## Testing

tests/GobchatEx.Core.Tests compiles `GobchatEx/Core/**/*.cs` and
`GobchatEx/Localization/**/*.cs` directly (no project reference) — any Dalamud
using-directive there breaks `dotnet test` (ADR 0002). Loc tests run against
throwaway resx fixtures (Fixtures/LocFixture*.resx). Dalamud-facing layer
(including the Chat 2 IPC path and range/group interactions) is validated by
manual in-game smoke test (docs/README.md).

## Design Records

docs/adr/: 0001 native-chat SeString rewriting · 0002 Dalamud-free parser core ·
0003 game sound effects only (v1). Roadmap: docs/ROADMAP.md (M1 done, M2/M3/M3.5
in progress).
