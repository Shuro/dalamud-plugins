<!-- Generated: 2026-07-03 | Files scanned: 43 | Token estimate: ~950 -->

# GobchatEx Architecture

Single-project Dalamud plugin (FFXIV via XIVLauncher). Three features, all in
the native chat log: RP segment recoloring (Say/Emote/OOC), mention
highlighting + sound (global triggers and per-character name matching), and
player-group sender-name coloring. No server, no database, no network I/O
(only local plugin IPC to Chat 2).

## Layers

```text
GobchatEx/
├── Plugin.cs            entry point, [PluginService] injection, commands,
│                        config migrations (v1→v3), native context menu
├── Configuration.cs     IPluginConfiguration; SegmentStyle, CharacterMentionSettings,
│                        PlayerGroup; staged-save via UpdateFrom()
├── Core/                matching engine — Dalamud-FREE (ADR 0002, test-enforced)
├── Chat/                Dalamud-facing: chat rewrite, groups, sound, Chat 2 IPC
├── Localization/        Loc.cs ResourceManager wrapper (also Dalamud-free)
├── Resources/           Language.resx + Language.de.resx (UI chrome only)
└── Windows/             ImGui settings UI (WindowSystem)
```

## Chat Pipeline (per message, framework thread)

Subscribed on `IChatGui.CheckMessageHandled` (fires after every plugin's
ChatMessage pass — GobchatEx formats last, no load-order race). Two
independent passes per message in `Chat/ChatListener.OnChatMessage`:

```text
1. Body highlighting        gate: RpHighlightEnabled + channel set
   extract TextPayload runs (indices + texts, parallel lists)
   → Core/MessageSegmenter.Segment      null = untouched fast path
       SegmentParser per TokenRule      precedence: OOC > Emote > Say (DefaultRules)
       MentionMatcher overlay           whole-word / partial / fuzzy, NFKC-folded
   → Chat/PayloadRewriter.Rewrite       spans → balanced UIForeground/UIGlow pairs
   → message.Message = new SeString     + SoundPlayer.TryPlay on mention (cooldown)

2. Sender group coloring    independent of the RP master switch/channels
   gate: not Tell/Echo/Error
   → Chat/SenderIdentity.Resolve        (name, world) from PlayerPayload or
                                        CrossWorld-icon-split text runs
   → Chat/FriendGroupLookup             (name, world) → friend-group index 0..6
   → Core/GroupMatcher.FindGroup        first active match wins: custom groups
                                        (member lists) before 7 friend groups
   → Chat/PayloadRewriter.RewriteUniform → message.Sender recolored
```

Derived state (segmenter, channel set, style/group lookups) is rebuilt only in
`ChatListener.SettingsChanged()` (and on Login/Logout), never per message.
No locking: chat handler and config UI both run on the framework thread.

## Mentions (Milestone 1)

Global trigger words union per-character resolved words for the logged-in,
remembered, active character (`IPlayerState.CharacterName`):

- Core/PlayerMentionResolver — full/first/last name (whole-word), partial
  substrings, Miqo'te apostrophe segments, custom words
- Core/StringSimilarity — OSA edit distance; FuzzyMatchLevel
  Conservative/Balanced/Aggressive budgets
- Core/UnicodeNormalizer — NFKC folding so decorative "fancy font" text matches

Characters are added explicitly ("Add Current Character" in MentionsTab),
never auto-learned on login.

## Player Groups (Milestone 2)

Custom groups (user-created, member = name + optional world, stored original-
casing, folded at match time) plus the game's 7 fixed friend-list display
groups (FfGroup 0=Star..6=Club, snapshot from FFXIVClientStructs
`InfoProxyFriendList` on login/load/manual refresh). Membership entry points,
all sharing `Chat/GroupMembershipActions` (mutates live config, not the
staged copy):

- Native right-click menu on any player name — `Plugin.OnMenuOpened` (IContextMenu)
- Chat 2's own ImGui context menu — `Chat/ChatTwoContextMenuIntegration`
  (ChatTwo.* IPC; inert when Chat 2 absent)
- `/gobchat group <idx|name> add|remove|clear player [world]` + `list` —
  `Chat/GroupCommandHandler` (regex grammar ported from the standalone app)
- Settings → Groups tab

## Commands

`/gobchat`, `/gobchatex`, `/gex` (hidden) → settings window (the only window;
OpenConfigUi and OpenMainUi too). First arg `group`/`g` routes to
GroupCommandHandler instead.

## Localization

Localization/Loc.cs — ResourceManager over Resources/Language.resx (en) with
de satellite; missing key renders the key itself. Culture follows Dalamud's
UI language unless Configuration.LanguageOverride is set; re-resolved via
`Plugin.RefreshLanguage()` after saves.

## Settings UI (Windows/)

- SettingsWindow.cs (245) — nav rail: General (GeneralTab + Profiles/Logs
  placeholders) / Appearance (FormattingTab) / Chat (MentionsTab, GroupsTab,
  RangeFilter placeholder) / About. Staged-save: edits go to a `mutable`
  Configuration copy; Save/Apply → `UpdateFrom(mutable)` → `Save()` →
  `ChatListener.SettingsChanged()`. Reopen re-stages (implicit cancel).
- MentionsTab.cs (339) — triggers, sound, character list + per-character flags
- GroupsTab.cs (265) — custom group CRUD + member lists; friend-group rows are
  color/active-only (never add/remove/rename)
- UiColorPicker.cs (112) — UIColor sheet swatch popup; SettingsUi.cs — shared widgets

## Key Files

- GobchatEx/Chat/ChatListener.cs (297) — event subscription, config-derived caches
- GobchatEx/Core/MentionMatcher.cs (180) — compiled regexes + fuzzy tokens, interval merge
- GobchatEx/Core/PlayerMentionResolver.cs (145) — name parts → whole/partial word lists
- GobchatEx/Chat/PayloadRewriter.cs (122) — span → payload translation (+ RewriteUniform)
- GobchatEx/Core/MessageSegmenter.cs (125) — pipeline orchestration + mention overlay
- GobchatEx/Core/SegmentParser.cs (112) — one TokenRule pass; ported from Gobchat
- GobchatEx/Core/GroupMatcher.cs (90) — ordered first-match group resolution

## Testing

tests/GobchatEx.Core.Tests compiles `GobchatEx/Core/**/*.cs` and
`GobchatEx/Localization/**/*.cs` directly (no project reference) — any Dalamud
using-directive there breaks `dotnet test` (ADR 0002). Loc tests run against
throwaway resx fixtures (Fixtures/LocFixture*.resx). Dalamud-facing layer is
validated by manual in-game smoke test (docs/README.md).

## Design Records

docs/adr/: 0001 native-chat SeString rewriting · 0002 Dalamud-free parser core ·
0003 game sound effects only (v1). Roadmap: docs/ROADMAP.md (M1 done, M2 in
progress).
