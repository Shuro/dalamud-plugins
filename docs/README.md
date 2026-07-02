# GobchatEx

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
- **Mentions** — your own list of trigger words/names (case-insensitive
  whole words), optionally with a game sound effect (`<se.1>`–`<se.16>`)
  and a per-sound cooldown.

Colors come from the game's UIColor palette (swatch picker in the config
window; right-click a swatch to clear). Delimiters may span item/player
links; an unclosed delimiter colors to the end of the message (matching the
original [GobchatEx](https://github.com/Shuro/GobchatEx) overlay's rules).
The message-rewriting approach follows
[ChatAlerts](https://github.com/Ottermandias/ChatAlerts). Design decisions
are recorded in [adr/](adr/); planned features migrating from the
standalone app are sequenced in [ROADMAP.md](ROADMAP.md).

## Commands

- `/gobchat` — Toggles the settings window (also reachable via
  `/xlplugins` → GobchatEx → ⚙, or the gear in the title bar).
- `/gobchatex`, `/gex` — Aliases for `/gobchat`.

## Building locally

This project uses the `Dalamud.NET.Sdk`, which auto-references everything
needed (`Dalamud.dll`, `DalamudPackager`, `Dalamud.Bindings.ImGui`,
`FFXIVClientStructs`, `Lumina`, `InteropGenerator.Runtime`).

```sh
dotnet build
```

That produces `bin/x64/Debug/GobchatEx/` — a packed plugin folder, not
just a DLL. To load it in-game:

1. `/xlsettings` → Experimental → "Dev Plugin Locations" → add the full path
   to `GobchatEx.dll` inside that folder.
2. `/xlplugins` → Dev Tools → Installed Dev Plugins → enable.
3. Hot-reload while iterating: `/xlreload GobchatEx`.

## Testing

The segmentation engine (`GobchatEx/Core/`) is Dalamud-free and covered by
unit tests (no game or XIVLauncher install needed):

```sh
dotnet test
# with coverage:
dotnet test --collect "Code Coverage;Format=cobertura"
```

Manual in-game smoke test (the Dalamud-facing layer is thin and validated
by hand): the **Echo** channel is highlighted by default (configs saved
before that change need "Reset to RP defaults" or a manual tick), and
mention tests need a trigger word added first. Then:

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
5. `/xlreload GobchatEx` — settings persist.

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
