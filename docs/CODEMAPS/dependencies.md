<!-- Generated: 2026-07-03 | Files scanned: 43 | Token estimate: ~450 -->

# Dependencies

## Plugin (GobchatEx/GobchatEx.csproj)

SDK: `Dalamud.NET.Sdk/15.0.0` — pins TargetFramework and implicitly references
(do NOT add HintPath references for these):

- Dalamud.dll (from `%AppData%\XIVLauncher\addon\Hooks\dev`)
- DalamudPackager (build output = packed plugin folder)
- Dalamud.Bindings.ImGui (settings UI)
- FFXIVClientStructs — `UIGlobals.PlayChatSoundEffect` (Chat/SoundPlayer.cs)
  and `InfoProxyFriendList` friend-list snapshot (Chat/FriendGroupLookup.cs)
- Lumina / Lumina.Excel — UIColor sheet (Windows/UiColorPicker.cs), World
  sheet (Chat/FriendGroupLookup.cs)
- InteropGenerator.Runtime

No NuGet PackageReferences of its own. `packages.lock.json` checked in
(Plogon/CI requirement). Manifest: GobchatEx/GobchatEx.json.

## Dalamud services used (Plugin.cs)

IChatGui (CheckMessageHandled rewrite) · ICommandManager ·
IDalamudPluginInterface (config persistence, UiBuilder, LanguageChanged, IPC)
· IContextMenu (Groups submenu on player-name right-click) · IClientState
(Login/Logout, IsLoggedIn) · IPlayerState (CharacterName for per-character
mentions, CurrentWorld for friend lookup) · IObjectTable (LocalPlayer for
own-message check) · IDataManager (Excel sheets) · IFramework
(RunOnFrameworkThread for friend-list refresh at load) · IPluginLog · plus
injected-but-idle: ITargetManager, ITextureProvider, INotificationManager.

## Plugin IPC (soft dependency)

Chat 2 (`ChatTwo.Register` / `Unregister` / `Available` / `Invoke`) —
Chat/ChatTwoContextMenuIntegration draws group toggles in Chat 2's own
context menu. Inert when Chat 2 is not installed; re-registers when it loads.

## Tests (tests/GobchatEx.Core.Tests.csproj)

net10.0, plain Microsoft.NET.Sdk — deliberately NOT referencing the plugin
project (keeps Dalamud out; compiles `Core/**` and `Localization/**` sources
directly, ADR 0002). Loc tests use embedded resx fixtures (Fixtures/).

- Microsoft.NET.Test.Sdk 17.12.0
- xunit 2.9.2 + xunit.runner.visualstudio 2.8.2
- FluentAssertions 7.2.0
- Coverage: built-in collector only — `dotnet test --collect "Code Coverage;Format=cobertura"`
  (coverlet cannot instrument net10.0; silently reports 0 lines)

## Local reference material (not build dependencies)

- `.references/Dalamud`, `.references/FFXIVClientStructs` — git submodules,
  API ground truth (see root CLAUDE.md)
- `.reference/ChatAlerts`, `.reference/GobchatEx`, `.reference/ChatTwo` —
  untracked source checkouts; payload-rewrite pattern, parser/mention/group
  ports, and the Chat 2 IPC contract (`.reference/ChatTwo/ipc.md`) originate here

## External services

None. No network calls, no telemetry, no database.
