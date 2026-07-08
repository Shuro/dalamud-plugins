# GobchatEx Roleplay Suite

A Dalamud plugin for Final Fantasy XIV via [XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher).

> **This is third-party software.** I am not affiliated with Square Enix.
> Please do not discuss this plugin in-game. Reports about plugin behaviour
> belong here on GitHub or in the Dalamud Discord — never in `/sh`, `/yell`,
> or party chat where it puts other players at unnecessary risk.

## What it does

**RP highlighting** in the native chat log, for roleplayers: chat messages
in configured channels are recolored per segment —

- **Say** — quoted speech: `"…"`, `„…“`, `„…”`, `“…”`, `»…«`, `«…»`
- **Emote** — actions: `*…*`, `<…>`
- **OOC** — out-of-character: `((…))`
- **Mentions** — your own list of trigger words (case-insensitive whole
  words) plus per-character player-name matching: full/first/last name,
  opt-in partial (substring) matching, Miqo'te apostrophe segments, and
  typo-tolerant fuzzy matching with three strictness levels. Decorative
  "fancy font" text is unicode-folded before matching. Optionally plays a
  game sound effect (`<se.1>`–`<se.16>`) with a per-sound cooldown.

Colors come from the game's UIColor palette (swatch picker in the config
window; right-click a swatch to clear). Delimiters may span item/player
links; an unclosed delimiter colors to the end of the message (matching the
original [GobchatEx](https://github.com/Shuro/GobchatEx) overlay's rules).
The message-rewriting approach follows
[ChatAlerts](https://github.com/Ottermandias/ChatAlerts). Design decisions
are recorded in [adr/](adr/); planned features migrating from the
standalone app are sequenced in [ROADMAP.md](ROADMAP.md).

**Player groups** recolor chat sender names per group: custom groups you
fill via right-click → Groups on any player name (also inside
[Chat 2](https://github.com/Infiziert90/ChatTwo)'s own context menu), the
`/gobchat group` command, or the settings tab — plus the game's seven
friend-list display groups (Star–Club). Custom groups take precedence over
friend groups. The settings window itself is localized (English, German)
and follows Dalamud's language unless overridden.

## Commands

- `/gobchat` — Toggles the settings window (also reachable via
  `/xlplugins` → GobchatEx Roleplay Suite → ⚙, or the gear in the title bar).
- `/gobchatex`, `/gex` — Aliases for `/gobchat`.
- `/gobchat group list` — Prints your custom groups with their indices.
- `/gobchat group <n|name> <add|remove> Player Name [World]` — Adds or
  removes a player from the custom group with 1-based index `n` or the
  given name; the bracketed world is optional (a bare entry matches the
  name on any world). `... clear` empties the group; `g` is a shorthand
  for `group`.

## Installing

There's no official [DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17)
listing yet. Until then, install via this repo's custom plugin repository:

1. In-game: `/xlsettings` → Experimental → Custom Plugin Repositories.
2. Add: `https://raw.githubusercontent.com/Shuro/GobchatEx-plugin/main/repo.json`
3. `/xlplugins` → search "GobchatEx" → Install.

Custom repositories get minimal support from the Dalamud team itself — if
you hit an install issue, open a GitHub issue here first.

## Building locally

This project uses the `Dalamud.NET.Sdk`, which auto-references everything
needed (`Dalamud.dll`, `DalamudPackager`, `Dalamud.Bindings.ImGui`,
`FFXIVClientStructs`, `Lumina`, `InteropGenerator.Runtime`).

```sh
dotnet build
```

That produces `GobchatEx/bin/Debug/` — the plugin DLL plus its manifest
(`GobchatExPlugin.json`), icon, and the German satellite. (A Release build
additionally packs `bin/Release/GobchatExPlugin/latest.zip` for distribution —
not needed for dev loading.) To load it in-game:

1. `/xlsettings` → Experimental → "Dev Plugin Locations" → add the full path
   to `GobchatExPlugin.dll` inside that folder.
2. `/xlplugins` → Dev Tools → Installed Dev Plugins → enable.
3. While iterating, enable **Automatic Reloading** on the plugin's row
   under Installed Dev Plugins — Dalamud then reloads the plugin whenever
   `dotnet build` rewrites the DLL.

## Testing

The matching engine (`GobchatEx/Core/`) and the localization helper
(`GobchatEx/Localization/`) are Dalamud-free and covered by unit tests (no
game or XIVLauncher install needed):

```sh
dotnet test
# with coverage:
dotnet test --collect "Code Coverage;Format=cobertura"
```

Manual in-game smoke test (the Dalamud-facing layer is thin and validated
by hand). Setup: the **Echo** channel is *not* highlighted by default
(defaults: Say, Custom Emote, Party, Cross-world Party) — tick it in the
highlighted-channels list first, and add a mention trigger word. Then:

1. `/echo he said "hi" and *waves* ((brb)) YourTriggerWord` — expect the
   emote, OOC and mention segments to recolor. The default Say color is
   white (UIColor row 1), so quotes only stand out in channels whose base
   color isn't white.
2. Repeat with an item link (Ctrl-click an item into the message) inside the
   quotes — the link keeps working and the quote color resumes after it.
3. Send the line twice — the next chat line must render in normal colors
   (balanced color payloads).
4. Add a trigger word, enable the mention sound, have someone (or an alt)
   say it — sound plays, respecting cooldown and the "not for my own
   messages" toggle.
5. Mentions tab → "Add Current Character" (it starts active), then
   `/echo Yourfirstname likes this` — your name recolors as a mention.
6. Right-click a player name in the chat log → Groups → add them to a
   custom group that has a color — their sender name recolors on their
   next chat line (group coloring skips Tell/Echo/Error, so use Say or
   Party).
7. Right-click the same player → Groups → untick the group — their next
   line renders in the normal sender color again. Re-add them by command:
   `/gobchat group list` prints the group indices, then
   `/gobchat group <n> add Player Name World` recolors them and
   `/gobchat group <n> remove Player Name World` clears it.
8. Put a friend (or an alt on your friend list) into one of the seven
   friend-list display groups (Star–Club) in the game's social window and
   give that friend group a color in the Groups tab — their sender name
   recolors. Add the same player to a colored custom group too — the
   custom group's color wins over the friend group's.
9. Change any setting and close the window right away — settings apply
   instantly and save automatically (no Save button; edits commit within
   half a second and closing flushes them). Rebuild (auto-reload) or
   toggle the plugin off and on — the change persists.
10. Range tab → enable the range filter with a short fade-out/cut-off, tick
    Say — have a distant alt say something: the line dims to a darkened
    step instead of your normal color, and vanishes (still visible, darkest
    step) once they're beyond the cut-off. Say something that mentions your
    trigger word from beyond the cut-off — with "mentions ignore range" on,
    it renders normally instead of dimmed. The Range tab also offers Custom
    Emote, Standard Emote, Yell and Shout — unformatted text on any of them
    fades using that channel's own configured Log Text Color (Character
    Configuration → Log Text Color) by default, darkened the same way, so
    Yell stays yellowish and Shout stays orange-red instead of collapsing
    into Say's grey; only channels with no game color configured fall back
    to a shared grey. If Chat 2 is currently installed *and loaded* and has
    its own customized color for that channel (its "Chat colours" page),
    that color is preferred instead — read directly from Chat 2's own
    config file, not live, so a color change there is picked up next time
    GEX's settings commit or you relog. Disabling or uninstalling Chat 2
    drops back to vanilla's color immediately (checked via Dalamud's
    installed-plugins list, not just whether the file exists — Chat 2's
    config file persists on disk even after it's disabled). Debug page →
    Range dimming → Step buttons
    demonstrate all of this directly without needing an alt: the
    "Unformatted text per channel" line prints one segment per channel in
    its own faded color, labeled with which source won (Chat 2 / vanilla /
    fallback).
11. Only if testing the Chat 2 styling integration (Milestone 3.5): load
    Chat 2's `local/dev-combined` fork build, open the ChatTwo tab in
    settings — it should show connected. Give a custom group a Chat 2
    background color: a group member's message gets that background in
    Chat 2's window (not the native log, which can't draw backgrounds).
    Repeat step 10's distance test with Chat 2 open — messages should fade
    to true partial transparency there instead of a darkened color step.
    Separately (works with any Chat 2 build, not just the fork): customize
    a channel's color on Chat 2's own "Chat colours" page, leave another
    channel at default, then use step 10's Debug page buttons — the
    customized channel's segment should be labeled "(Chat 2)" and show that
    exact color faded; the untouched channel should still show "(vanilla)".

## Contributing

Issues and PRs welcome — development setup, commands, and the PR checklist
are in [CONTRIBUTING.md](CONTRIBUTING.md). If a PR involves AI assistance beyond autocomplete,
disclose it in the PR description using the official Dalamud AI policy's
levels ([dalamud.dev/plugin-publishing/ai-policy](https://dalamud.dev/plugin-publishing/ai-policy)):

- **None/Hint** — no AI, or autocomplete only. No disclosure needed.
- **Assist** — human-led; AI used for specific tasks on demand.
- **Pair** — active collaboration; roughly equal contribution.
- **Copilot** — AI implements; human plans and reviews.
- **Auto** — AI acts autonomously with minimal human direction.

Test your changes yourself and be able to explain what they do — "the AI
did it" isn't an acceptable answer during review. The official Dalamud
plugin repository ([DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17))
auto-rejects entirely AI-generated submissions and bans undisclosed AI use
in demonstrably AI-written work. See [AI-DISCLOSURE.md](AI-DISCLOSURE.md)
for this project's own AI-usage history.

## License

AGPL-3.0-or-later. See [LICENSE](../LICENSE).
