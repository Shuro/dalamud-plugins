<!-- Generated: 2026-07-06 | Files scanned: 57 | Token estimate: ~600 -->

# Dependencies

## Plugin (GobchatEx/GobchatEx.csproj)

SDK: `Dalamud.NET.Sdk/15.0.0` — pins TargetFramework and implicitly references
(do NOT add HintPath references for these):

- Dalamud.dll (from `%AppData%\XIVLauncher\addon\Hooks\dev`)
- DalamudPackager (build output = packed plugin folder)
- Dalamud.Bindings.ImGui (settings UI)
- FFXIVClientStructs — `UIGlobals.PlayChatSoundEffect` (Chat/SoundPlayer.cs)
  and `InfoProxyFriendList` friend-list snapshot (Chat/FriendGroupLookup.cs,
  Windows/SettingsTabs/DebugGroupsPane.cs)
- Lumina / Lumina.Excel — UIColor sheet (Windows/UiColorPicker.cs,
  Chat/UiColorDimmer.cs, Windows/SettingsTabs/DebugRangePane.cs), World
  sheet (Chat/FriendGroupLookup.cs)
- InteropGenerator.Runtime

No NuGet PackageReferences of its own. `packages.lock.json` checked in
(Plogon/CI requirement). Manifest: GobchatEx/GobchatExPlugin.json.

## Dalamud services used (Plugin.cs)

IChatGui (CheckMessageHandled rewrite) · ICommandManager ·
IDalamudPluginInterface (config persistence, UiBuilder, LanguageChanged, IPC)
· IContextMenu (Groups submenu on player-name right-click) · IClientState
(Login/Logout, IsLoggedIn, ActivePluginsChanged for Chat 2 disable
detection) · IPlayerState (CharacterName for per-character mentions,
CurrentWorld/HomeWorld for friend + range lookups) · IObjectTable
(LocalPlayer + player enumeration for the range filter's distance lookups)
· IDataManager (Excel sheets) · IGameConfig (Formatting tab's "import game
channel color") · IFramework (RunOnFrameworkThread for friend-list refresh;
periodic distance-snapshot refresh for Chat 2 styling) · IAddonLifecycle
("FriendList" addon PostRequestedUpdate → live friend-group refresh,
Chat/FriendListAddonListener) · IPluginLog · plus injected-but-idle:
ITargetManager, ITextureProvider, INotificationManager.

## Plugin IPC (soft dependencies)

- **Chat 2 context menu** (`ChatTwo.Register` / `Unregister` / `Available` /
  `Invoke`) — Chat/ChatTwoContextMenuIntegration draws group toggles in
  Chat 2's own context menu.
- **Chat 2 message styling** (Milestone 3.5; `ChatTwo.StyleVersion` /
  `SetMessageStyleProvider` / `Available` / `GetTabs` / `TabsChanged` /
  `SetTabStylePolicies`) — Chat/ChatTwoStyleProvider registers a per-message
  style callback gate (`GobchatEx.MessageStyle`) for backgrounds and true
  alpha; `#if DEBUG` builds can swap in Chat/ChatTwoStyleIpcTester instead.
  Contract: `.reference/ChatTwo/ipc.md`; developed against a
  `local/dev-combined` Chat 2 fork build (ChatTwo#186, PRs #187–#189) ahead
  of upstream release.

Both are inert when Chat 2 is not installed or the IPC gate is unsupported;
re-probed on `ChatTwo.Available` and on Dalamud's `ActivePluginsChanged`
(Chat 2 disable/unload has no IPC callback of its own).

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
