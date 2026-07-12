# Plan: Renaming the repo to a neutral name (e.g. "dalamud-plugins") + light multi-plugin readiness

> **Status (2026-07-12): executed.** GitHub repo renamed to `Shuro/dalamud-plugins`, hardcoded URLs updated, `Directory.Build.props` + `global.json` added (Part 2 item 1). Old-name references below are kept as the historical record. CI matrix / repo.json upsert / `src/` move remain deferred per the YAGNI line.

## Context

The repo is currently named after its one plugin (`GobchatEx-plugin`), containing exactly one Dalamud plugin project (`GobchatEx`). The user is weighing generalizing the container name (e.g. to `dalamud-plugins`) so the repo could eventually host more than one plugin. This document is an informational breakdown — not an execution checklist — covering what the rename actually touches, and separately, what "ready for a second plugin" would take. These are two independent decisions; either can happen without the other.

This is exploratory research, not a commitment to execute. Nothing below should be run without a separate, explicit go-ahead — particularly the GitHub repo rename itself, which is a shared/external-system change.

## The key finding: plugin identity is already decoupled from the folder/repo name

`GobchatEx/GobchatEx.csproj:5-6` explicitly pins `AssemblyName=GobchatExPlugin` and `RootNamespace=GobchatEx` — a comment there (lines 34-37) confirms this was done deliberately so DalamudPackager's derived `InternalName` wouldn't depend on the project filename. The manifest `GobchatEx/GobchatExPlugin.json` matches that AssemblyName exactly. None of this derives from or references the repo folder name `GobchatEx-plugin` in any way.

**Practical upshot:** renaming the container is a rename of the box, not the thing inside it. It does not touch `InternalName`, C# namespaces, users' Dalamud config directories, or the plugin's identity chain at all. That's the single biggest simplifier here — this is not a plugin-identity migration (see the `dalamud-plugin-assemblyname-identity` skill for what that harder case looks like), it's a container/URL rename.

## Part 1 — Renaming the folder + GitHub repo

### Local folder rename
Trivial and reversible. No `.csproj`, `.slnx`, workflow, or script derives a path from the root folder's name (confirmed by grep across tracked files). Just rename the directory.

One thing outside the repo to remember: Dalamud's local "Dev Plugin Locations" setting (used for hot-reload dev testing) points at an absolute filesystem path to this folder on your machine. After a folder rename, that entry needs re-pointing in the Dalamud/XIVLauncher settings UI, or the dev-loaded plugin won't resolve until you do.

### GitHub repository rename
The rename itself (GitHub Settings → repository name, or `gh repo rename`) is an external, shared-state action — flag it clearly as something to confirm deliberately, not fold into a routine commit.

De-risking factor: GitHub automatically redirects the old repo name to the new one for git operations (clone/fetch/push) and the main web URL, and `raw.githubusercontent.com` content URLs follow the same redirect. So nothing breaks the instant the rename happens — but redirects aren't a permanent guarantee (they can be lost if the old name is later claimed by someone else), so the hardcoded references below are still worth updating rather than relying on redirects indefinitely.

Concrete places the old name/URL is hardcoded and would need manual updates:

| File | What | Self-heals on release? |
|---|---|---|
| `repo.json:9,21,24-25` | RepoUrl, IconUrl, DownloadLinkInstall/Update | DownloadLink fields yes (built from `github.repository` in `release.yml`); RepoUrl/IconUrl **no** — copied verbatim from `GobchatExPlugin.json` |
| `GobchatEx/GobchatExPlugin.json:6,10` | RepoUrl, IconUrl (source of truth `repo.json` copies from) | No — manual edit required |
| `GobchatEx/GobchatEx.csproj:8` | PackageProjectUrl | No |
| `GobchatEx/Windows/SettingsTabs/AboutTab.cs:15` | RepoUrl const, shown in the plugin's own UI to end users | No |
| `docs/README.md:60` | Hardcoded raw.githubusercontent.com install URL for the custom XIVLauncher repo link | No |
| git `origin` remote | `git@github.com:Shuro/GobchatEx-plugin.git` | N/A — update via `git remote set-url`, or rely on redirect |

Blast radius is low right now: per project memory, GEX is unpublished (no DalamudPluginsD17 listing), so the only people affected by a stale `repo.json` URL are you and any beta testers who manually added the current raw URL as a custom plugin repo — and those keep working via GitHub's redirect in the meantime.

## Part 2 — Light multi-plugin scaffolding

### Already generalizes with zero changes (confirmed by exploration)
- **`GobchatEx.slnx`** — already a multi-project solution (references `GobchatEx/GobchatEx.csproj` + `tests/GobchatEx.Core.Tests/...`). Adding a second plugin is one more `<Project Path="...">` line.
- **`repo.json`** — already a JSON array; `scripts/generate-repo.ps1:49` deliberately forces the array shape (comment warns against PowerShell unrolling a single-element array). Appending a second manifest entry is structurally free.
- **`.references/Dalamud` and `.references/FFXIVClientStructs` submodules** — not wired into any `.csproj` (no ProjectReference/Reference/Compile-Include found anywhere) — they're pure grep-reference documentation per the root `CLAUDE.md`. Any number of sibling plugins share them with zero build config.
- **`.editorconfig`** — `root = true`, already applies repo-wide regardless of project count.

### Needs real work before a second plugin can actually ship
1. **No `Directory.Build.props` exists.** Worth introducing at the repo root to hold settings every plugin project should share (Dalamud.NET.Sdk version pin, TargetFramework, Nullable/LangVersion, etc.), extracted from `GobchatEx/GobchatEx.csproj`'s current settings. Purely additive — no behavior change with just one plugin, but removes duplication once a second one exists.
2. **`tests/GobchatEx.Core.Tests/GobchatEx.Core.Tests.csproj`** compiles source directly via `<Compile Include="..\..\GobchatEx\Core\**\*.cs">`-style includes (~lines 17, 25) instead of a ProjectReference — hardwired to `GobchatEx` specifically. A second plugin would bring its own analogous test project following the same convention, not reuse this one.
3. **CI workflows hardcode single-project paths** — this is the real work:
   - `build.yml` runs tests against the literal path `tests/GobchatEx.Core.Tests/GobchatEx.Core.Tests.csproj`.
   - `release.yml` hardcodes `GobchatEx/GobchatEx.csproj` (version-tag check), `GobchatEx/bin/Release/GobchatExPlugin/latest.zip` (release asset), and `GobchatExPlugin.json` (manifest path for repo.json generation).
   - None of this generalizes today; supporting independent releases of multiple plugins needs either a build matrix (one job per plugin directory) or a tag-prefix/path-based parameterization.
4. **`repo.json` generation currently overwrites the whole array** from one manifest each release, rather than upserting one entry among several. With 2+ independently-releasing plugins, this needs to become "load existing `repo.json`, replace this plugin's entry, write back the full array" instead of clobbering other plugins' entries on every release.
5. **Optional cosmetic move:** `GobchatEx/` → `src/GobchatEx/` (or `plugins/GobchatEx/`) so the root reads as "a monorepo of N plugins" once there's an actual second one. Not required — `.slnx` references by relative path either way — purely about root-level readability.

## Part 3 — VSCode and Claude Code local tooling state

Renaming the folder only touches what's *inside* the repo. Several local tools key state to the repo's **absolute path**, outside the repo entirely, and won't know it moved. This part covers those — confirmed by inspecting this machine's actual state, not assumed.

### VSCode
No `.vscode/` folder exists in this repo (confirmed earlier), so there's no in-repo workspace config to edit. VSCode's own per-folder state (recent-workspaces list, the "do you trust the authors of this folder" flag, per-workspace storage under `%APPDATA%\Code\User\workspaceStorage\<hash>\`) is keyed by the absolute path outside the repo. After the rename, just reopen the folder from its new path via File → Open Folder — VSCode treats it as a new workspace, re-asks the trust prompt once, and starts fresh workspace storage (there's nothing repo-specific stored there to lose, since no `.vscode/settings.json` exists). The stale "Recent" entry for the old path is harmless.

Separately, recommend a clean rebuild after the move: `dotnet clean` then `dotnet build GobchatEx.slnx`. MSBuild's `obj/`/`bin/` intermediate folders can cache absolute paths for incremental builds, and that's the most common source of confusing "phantom" errors right after a folder move.

### Claude Code — this is where "memories" actually live
Claude Code's project-scoped memory (the auto-memory system building up notes like the ones in `MEMORY.md`) is stored at `C:\Users\Shuro\.claude\projects\<encoded-path>\memory\`, where `<encoded-path>` is a direct, mechanical encoding of the absolute repo path (lowercase drive letter, `:` and `\`/spaces → `-`). Today that's `g--Visual-Studio-Code-Projects-GobchatEx-plugin`. Claude Code has no notion that a folder "moved" — renaming it means the new path encodes to a **different** directory name, and Claude Code will start a brand-new, empty memory store there. The 10 existing memory files (the `MEMORY.md` index plus `net10-coverage-tooling.md`, `references-are-api-ground-truth.md`, `dalamud-dev-plugin-autoreload.md`, `upstream-postings-need-approval.md`, `chattwo-styling-ipc-project.md`, `gobchatex-dev-build-is-debug.md`, `gex-terminology.md`, `uicolor-sheet-ground-truth.md`, `gex-unpublished-no-migration.md`, `m4-profiles-research.md`) would simply be orphaned, not migrated automatically.

**This already happened once to this exact project.** `C:\Users\Shuro\.claude\projects\g--Visual-Studio-Code-Projects-GobchatEx\` (no `-plugin` suffix) still exists from before this repo's prior folder rename (`GobchatEx` → `GobchatEx-plugin`) — it holds old session transcripts and has no `memory/` subfolder, confirming memory hadn't accumulated yet at that point (so nothing was actually lost last time) — but it's live proof of exactly this mechanism, and this time there *is* real memory content to lose.

Fix: after renaming the folder to (e.g.) `dalamud-plugins`, the new project directory Claude Code will use is `C:\Users\Shuro\.claude\projects\g--Visual-Studio-Code-Projects-dalamud-plugins\` (same encoding rule applied to the new path). Copy the old `memory/` directory's contents into that new location — it's a plain file copy, there's no index or database to update, Claude Code just reads whatever `.md` files sit there. If keeping old sessions resumable under the new path also matters (`/resume-session`, `/sessions`), copy the whole project directory (the `.jsonl` transcripts plus `subagents/`/`tool-results/` subfolders), not just `memory/`.

One more spot, lower stakes: `~/.claude.json` keys a small per-project settings block to the literal absolute path (confirmed at line 763 — `"g:/Visual Studio Code Projects/GobchatEx-plugin": {...}`), holding `allowedTools`, MCP server config, `hasTrustDialogAccepted`, and `loggedAuthoredArtifactPaths` (which of this repo's checked-in skill files Claude Code has already logged as authored). This block would also become orphaned by the rename. Practically low-cost here — `allowedTools`/`mcpServers` are currently empty for this project — so the only visible effect is the trust dialog reappearing once and the 8-entry `loggedAuthoredArtifactPaths` list resetting. Worth a manual copy-and-rekey of that JSON block only if avoiding a one-time trust prompt actually matters; otherwise it's fine to let it rebuild naturally.

## Suggested sequencing, if/when you proceed

1. Rename the local folder — zero risk, reversible, do anytime.
2. Immediately after, copy `memory/` (and optionally the rest of the project directory) from the old encoded Claude Code project path to the new one, per Part 3 — do this before much new work happens in the renamed folder, so nothing new gets written to the old, soon-to-be-stale memory store.
3. Rename the GitHub repo — only after an explicit, separate confirmation; then update the git `origin` remote.
4. Manually update the 5 hardcoded URL spots (table above) rather than relying on GitHub's redirect long-term.
5. Add `Directory.Build.props` extracting today's shared `.csproj` settings — safe and additive even with a single plugin.
6. Leave the CI matrix-ification and `repo.json` upsert logic (items 3-4 in Part 2) until an actual second plugin exists to write and verify them against — building that logic speculatively now means a matrix of one, untestable until there's a real second target (this is the YAGNI line: everything above it is free/safe now, everything at/after it needs a real second plugin to validate against).

## Verification, if executed later

- After the folder/repo rename: `dotnet build GobchatEx.slnx` and `dotnet test tests/GobchatEx.Core.Tests/` still succeed from the new path/remote.
- Load the plugin via Dalamud's dev plugin location (repointed to the new path) and confirm it starts.
- Open the in-game Settings → About tab and confirm the repo link (`AboutTab.cs`) resolves to the new URL after that edit.
- Diff `repo.json` after the next tagged release to confirm the self-healing `DownloadLink*` fields match the new repo name, and that the manually-edited `RepoUrl`/`IconUrl` fields were updated too.
- If `Directory.Build.props` is added: confirm `dotnet build` output is unchanged (same assembly name, same warnings) — it should be a no-op refactor for the single existing plugin.
