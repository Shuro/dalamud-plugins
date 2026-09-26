# 6. Prefer platform libraries over hand-rolled equivalents

Date: 2026-07-22

## Status

Accepted

## Context

DalamudPluginsD17 review of the initial submission caught the plugin
hand-encoding SeString color-macro bytes (`Chat/SeStringColorMacro.cs`) when
Lumina's `SeStringBuilder` already owns that encoding — fixed in commit
`7748092` (see ADR 0001, amendment 2026-07-21). That raised the broader
question: does the plugin reimplement anything else that the platform
libraries (Dalamud.dll, Lumina, FFXIVClientStructs, the ImGui bindings)
already ship?

A full audit on 2026-07-22 of `Chat/`, `Windows/`, `Config/`, `Localization/`
and `Plugin.cs` against the Dalamud source in `.references/` found no second
case. File and folder picking goes through Dalamud's `FileDialogManager`,
ImGui styling through `ImRaii`/`ImGuiHelpers`, textures through
`ITextureProvider`, sender identity through `PlayerPayload` (manual text-run
parsing only as a fallback for payload-less senders, which no platform API
covers), Excel data through `IDataManager.GetExcelSheet<>`, and SeStrings
through Lumina's builder. The only raw payload bytes left are the `#if DEBUG`
renderer probes in DebugTab.cs, which exist to test the renderer directly.

## Decision

When the plugin needs functionality the platform libraries provide, use the
library. Hand-rolling is reserved for cases where no platform API exists or
where a documented product constraint rules the library out — and every such
divergence must carry a code comment explaining why.

The sanctioned divergences as of this audit:

- `Chat/ChatLogChannelNames.cs` keeps its own channel-name table instead of
  `XivChatTypeExtensions.GetDetails()?.FancyName`: log files are a long-term
  archive, so names must not shift with Dalamud enum renames (comment at the
  top of the class).
- `Config/Configuration.cs` persists per-section JSON files instead of
  `SavePluginConfig`: the settings window's per-section change detection and
  granular commits require one file per section and an identical serializer
  (comment on `Sections`/`Serialize`).
- `Chat/UiColorDimmer.cs` keeps its own dimming math: Dalamud's
  `ColorHelpers.Darken` is HSV-subtractive in ImGui byte order, while range
  fading needs a hue-preserving channel multiply on packed `0xRRGGBBAA` —
  different math, not a duplication.
- `Core/**` stays free of platform types entirely (`RgbaColor`,
  `StringSimilarity` instead of Dalamud's `FuzzyMatcher`,
  `UnicodeNormalizer`): ADR 0002 keeps the engine compilable without
  Dalamud so the unit tests run outside the game.

## Consequences

- New code reaches for `.references/` first (per CLAUDE.md) to check what the
  platform already offers before implementing.
- Reviewers finding a hand-rolled construct can treat an undocumented one as
  a defect; the four cases above are decisions, not oversights.
- The list above is expected to shrink, not grow: adding a divergence means
  adding its rationale here and in the code.
