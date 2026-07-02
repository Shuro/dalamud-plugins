<!-- Generated: 2026-07-02 | Files scanned: 31 | Token estimate: ~650 -->

# GobchatEx Architecture

Single-project Dalamud plugin (FFXIV via XIVLauncher). RP highlighting: recolors
Say/Emote/OOC/Mention segments in the native chat log and plays an optional
mention sound. No server, no database, no network I/O.

## Layers

```
GobchatEx/
├── Plugin.cs            entry point, [PluginService] injection, commands, lifecycle
├── Configuration.cs     IPluginConfiguration + SegmentStyle; staged-save via UpdateFrom()
├── Core/                segmentation engine — Dalamud-FREE (ADR 0002, test-enforced)
├── Chat/                Dalamud-facing: chat event, payload rewrite, sound
└── Windows/             ImGui settings UI (WindowSystem)
```

## Chat Pipeline (per message, framework thread)

```
IChatGui.ChatMessage
  → Chat/ChatListener.OnChatMessage      gate: RpHighlightEnabled + channel set
      extract TextPayload runs (indices + texts, parallel lists)
  → Core/MessageSegmenter.Segment        null = untouched fast path
      SegmentParser per TokenRule        precedence: OOC > Emote > Say (DefaultRules)
      delimiter open-state spans runs;   unclosed delimiter colors to message end
      MentionMatcher overlay             regex whole-word, merged intervals, on top
  → Chat/PayloadRewriter.Rewrite         spans → balanced UIForeground/UIGlow on/off pairs
  → message.Message = new SeString(...)  + SoundPlayer.TryPlay on mention (cooldown)
```

Derived state (segmenter, channel set, style lookup) is rebuilt only in
`ChatListener.SettingsChanged()`, never per message. No locking: chat handler
and config UI both run on the framework thread.

## Commands

`/gobchat`, `/gobchatex`, `/gex` (hidden) → `Plugin.ToggleSettingsUI()` — the
settings window is the plugin's only window (OpenConfigUi and OpenMainUi too).

## Settings UI (Windows/)

- SettingsWindow.cs (237) — nav rail (General/Appearance/Chat/About) + footer.
  Staged-save: edits go to a `mutable` Configuration copy; Save/Apply →
  `Configuration.UpdateFrom(mutable)` → `Save()` → `ChatListener.SettingsChanged()`.
  Window reopen re-stages from saved config (implicit cancel).
- SettingsTabs/ — ISettingsTab impls: GeneralTab, FormattingTab (styles +
  channels), MentionsTab (triggers + sound), AboutTab, PlaceholderTab (roadmap stubs).
- UiColorPicker.cs (111) — UIColor sheet swatch popup; SettingsUi.cs — shared widgets.

## Key Files

- GobchatEx/Chat/ChatListener.cs (135) — event subscription, config-derived caches
- GobchatEx/Core/MessageSegmenter.cs (120) — pipeline orchestration + mention overlay
- GobchatEx/Core/SegmentParser.cs (112) — one TokenRule pass; ported from Gobchat
- GobchatEx/Core/MentionMatcher.cs (94) — compiled regexes, interval merge
- GobchatEx/Chat/PayloadRewriter.cs (84) — span → payload translation (ChatAlerts pattern)
- GobchatEx/Core/{SegmentSpan,SegmentType,TokenRule,DefaultRules}.cs — value types + fixed rules

## Testing

tests/GobchatEx.Core.Tests compiles `GobchatEx/Core/**/*.cs` directly (no
project reference) — any Dalamud using-directive in Core breaks `dotnet test`.
Dalamud-facing layer is validated by manual in-game smoke test (docs/README.md).

## Design Records

docs/adr/: 0001 native-chat SeString rewriting · 0002 Dalamud-free parser core ·
0003 game sound effects only (v1). Roadmap: docs/ROADMAP.md.
