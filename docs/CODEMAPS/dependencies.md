<!-- Generated: 2026-07-12 (v0.9.0) | Files scanned: 73 (+19 tests) | Token estimate: ~750 -->

# Dependencies

## Plugin (GobchatEx/GobchatEx.csproj)

SDK: `Dalamud.NET.Sdk/15.0.0` — pins TargetFramework and implicitly references
(do NOT add HintPath references for these):

- Dalamud.dll (from `%AppData%\XIVLauncher\addon\Hooks\dev`)
- DalamudPackager (build output = packed plugin folder)
- Dalamud.Bindings.ImGui (settings UI, Quickbar; incl. `ImGuiP` internals —
  `FindWindowByName` for the Quickbar's Chat 2 window anchor)
- FFXIVClientStructs — `UIGlobals.PlayChatSoundEffect` (Chat/SoundPlayer.cs)
  and `InfoProxyFriendList` friend-list snapshot (Chat/FriendGroupLookup.cs,
  Windows/SettingsTabs/DebugGroupsPane.cs)
- Lumina / Lumina.Excel — UIColor sheet, dim-only (Chat/UiColorDimmer.cs
  darker-row remap of game-embedded link colors, DebugRangePane swatches —
  the plugin's own colors are raw RGBA, Core/RgbaColor +
  Chat/SeStringColorMacro), World sheet (Chat/FriendGroupLookup.cs)
- Newtonsoft.Json (ships with Dalamud) — config persistence; JObject parse of
  Chat 2's config file (Chat/ChatTwoChannelColors.cs)
- InteropGenerator.Runtime

Own NuGet PackageReferences (ADR 0004, custom mention sounds — all managed;
Windows' ogg codecs are an optional store package):

- NAudio 2.2.1 (wav/mp3 decode + WaveOutEvent playback)
- NAudio.Vorbis 1.5.0 (ogg/vorbis via NVorbis)
- Concentus 2.2.2 + Concentus.Oggfile 1.0.7 (ogg/opus — Discord-saved sounds)

`packages.lock.json` checked in (Plogon/CI requirement). Manifest:
GobchatEx/GobchatExPlugin.json.

## Dalamud services used (Plugin.cs)

IChatGui (CheckMessageHandled: ChatListener rewrite, ChatLogger,
LegacyCommandListener) · ICommandManager (/gex + aliases) ·
IDalamudPluginInterface (config persistence, UiBuilder + UI-hide exemption
flags, LanguageChanged, IPC, InstalledPlugins) · IContextMenu (Groups
submenu on player-name right-click) · IClientState (Login/Logout,
IsLoggedIn, ActivePluginsChanged for Chat 2 disable detection) · ICondition
(Quickbar hide conditions: combat/cutscene/loading) · IPlayerState
(CharacterName for per-character mentions + log sessions,
CurrentWorld/HomeWorld) · IObjectTable (LocalPlayer + player enumeration:
range filter, /gex player) · ITargetManager (`<t>` resolution in
CommandDispatcher; GroupsTab add-current-target) · IDataManager (Excel
sheets) · IGameConfig (import game channel color; range fade's
channel-native text color) · IGameGui (ChatLog addon position for the
Quickbar's attach mode) · IFramework (RunOnFrameworkThread; distance-snapshot
timer; ChatLogger flush tick) · IAddonLifecycle ("FriendList"
PostRequestedUpdate → live friend-group refresh) · ITextureProvider (About
tab icon) · IPluginLog.

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

- **Chat 2 channel colors (file read, NOT IPC)** — Chat/ChatTwoChannelColors.cs
  reads stock Chat 2's persisted per-channel colors straight from ChatTwo.json
  in the shared pluginConfigs folder (works without the fork PRs). Gated on a
  live `InstalledPlugins` IsLoaded check; convention not contract — degrades
  to empty on any failure.
- **Chat 2 ImGui window (read-only, shared context)** — the Quickbar's attach
  mode reads Chat 2's main window position via `ImGuiP.FindWindowByName("###chat2")`;
  absent Chat 2 it falls back to the game's ChatLog addon (IGameGui).

## File system output

Chat logger (Milestone 5) appends per-session .log files to a user-chosen
folder (no default — logging stays disabled until one is picked; hand-edited
relative paths are contained by Core/Util/PathSecurityUtil). Manual
start/stop only.
Custom mention sounds are read (never written) from a user-picked audio file.

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
  untracked source checkouts; payload-rewrite pattern, parser/mention/group/
  chat-logger ports, and the Chat 2 IPC contract originate here

## External services

None. No network calls, no telemetry, no database.
