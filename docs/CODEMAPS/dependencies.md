<!-- Generated: 2026-07-02 | Files scanned: 31 | Token estimate: ~350 -->

# Dependencies

## Plugin (GobchatEx/GobchatEx.csproj)

SDK: `Dalamud.NET.Sdk/15.0.0` — pins TargetFramework and implicitly references
(do NOT add HintPath references for these):

- Dalamud.dll (from `%AppData%\XIVLauncher\addon\Hooks\dev`)
- DalamudPackager (build output = packed plugin folder)
- Dalamud.Bindings.ImGui (settings UI)
- FFXIVClientStructs — used for `UIGlobals.PlayChatSoundEffect` (Chat/SoundPlayer.cs)
- Lumina / Lumina.Excel — UIColor sheet (Windows/UiColorPicker.cs)
- InteropGenerator.Runtime

No NuGet PackageReferences of its own. `packages.lock.json` checked in
(Plogon/CI requirement). Manifest: GobchatEx/GobchatEx.json.

## Dalamud services used (Plugin.cs)

IChatGui (ChatMessage rewrite) · ICommandManager · IDalamudPluginInterface
(config persistence, UiBuilder) · IObjectTable (LocalPlayer for own-message
check) · IPluginLog · plus injected-but-idle: IClientState, IPlayerState,
IDataManager, IFramework, ITextureProvider, INotificationManager.

## Tests (tests/GobchatEx.Core.Tests.csproj)

net10.0, plain Microsoft.NET.Sdk — deliberately NOT referencing the plugin
project (keeps Dalamud out; compiles Core sources directly).

- Microsoft.NET.Test.Sdk 17.12.0
- xunit 2.9.2 + xunit.runner.visualstudio 2.8.2
- FluentAssertions 7.2.0
- Coverage: built-in collector only — `dotnet test --collect "Code Coverage;Format=cobertura"`
  (coverlet cannot instrument net10.0; silently reports 0 lines)

## Local reference material (not build dependencies)

- `.references/FFXIVClientStructs` — git submodule (struct research)
- `.reference/ChatAlerts`, `.reference/GobchatEx` — untracked source checkouts;
  payload-rewrite pattern and parser port originate here

## External services

None. No network calls, no telemetry, no database.
