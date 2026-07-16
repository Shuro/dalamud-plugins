# 5. Per-group alert sounds

Date: 2026-07-15

## Status

Accepted

Extends [4. Custom mention sound files via NAudio](0004-custom-sound-files-naudio.md)

## Context

Milestone 6 adds "ding when my RP partner speaks": an optional alert sound per
player group. Group identity is already resolved per message by the
group-coloring pass (`ChatListener.ApplySenderGroupColor` →
`GroupMatcher.FindGroup`), which is why the per-*group* tier is cheap — unlike
per-*trigger* mention sounds, where `MentionMatcher` merges all matches into
anonymous spans before any caller could route a sound (rejected in the 2026-07
further-features review, see ROADMAP "Considered, not planned"). A message can
now qualify for two sounds at once (a group member mentioning you), and
`SoundPlayer` cached exactly one loaded file, so both needed policy and
plumbing decisions.

## Decision

- **Config:** the alert-sound quartet (source, effect, file path, volume) is
  extracted into `IAlertSoundSettings`; `PlayerGroup` implements it directly
  (new optional fields, default off — purely additive, no migration),
  `MentionsConfig` maps its historical `MentionSound*` JSON names onto it
  explicitly so serialized keys stay unchanged. The settings UI reuses one
  shared `AlertSoundEditor` for both.
- **At most one sound per message; the mention alert wins.** A mention is the
  more specific "you were addressed" signal. When a message matches a mention
  *and* the mention sound is enabled, the group sound stands down — even if
  the mention sound then loses to its own cooldown (deliberate: the tie-break
  is per message and must not depend on timer state). With the mention sound
  disabled, group sounds play on mention messages too.
- **One shared cooldown across all groups** (`GroupsConfig.GroupSoundCooldownMs`,
  default 5 s), separate from the mention cooldown. The cooldown is spam
  protection, not a per-group rhythm — per-group timers would turn N groups
  into N overlapping dings in busy scenes.
- **Own messages never play a group sound**, with no opt-out toggle (unlike
  the mention sound's `SuppressSoundFromSelf`): hearing your own group's ding
  on every line you send is never useful.
- **Channel scope = group-coloring scope** (`GroupingExcludedChannels`): the
  sound fires wherever the group recolor would, independent of whether the
  group actually recolors anything (a group may be sound-only).
- **`SoundPlayer` generalizes its single cached file to a path-keyed cache**
  (the multi-sound cache ADR 0003 forecast, modeled on ChatAlerts'
  `AlertCache`), capped at 8 entries with wholesale reset instead of LRU — a
  real config holds a handful of short files. Mention and group alerts keep
  separate cooldown timers but share the cache; volume is applied per play, so
  two alerts sharing one file can carry different volume sliders.
- **Friend groups have no sound UI yet.** The engine treats them like any
  group (they are `PlayerGroup`s), but the Groups tab's friend-group table has
  no room for the editor; hand-edited `groups.json` sound fields on a friend
  group do work. UI can follow if asked for.

## Consequences

- The Groups tab gains a per-group sound row (shared editor, volume included)
  and one always-visible shared-cooldown slider, disabled until any group
  sound is on.
- `ChatListener.ApplyBodyHighlighting` now reports whether the body matched a
  mention, so the group pass reuses the existing segmentation instead of
  probing a second time.
- No unit tests: the policy lives in the Dalamud-facing `Chat/` layer (per
  ADR 0002, Core stays the tested part); the in-game smoke test covers it.

## Amendment (2026-07-16)

The "channel scope = group-coloring scope (`GroupingExcludedChannels`)" bullet
was wrong: that 4-entry denylist (`TellIncoming`/`TellOutgoing`/`Echo`/
`ErrorMessage`, carried over from the old app's much smaller `ChatChannel`
enum) let every senderless system/notification `XivChatType` — Teleport
completion, zone-leave text, Party Finder recruitment notices, gil-spent
messages, plus the whole combat/loot/craft log — reach `GroupMatcher.FindGroup`
via a degenerate `("", currentWorld)` identity, firing group recolors and
sounds on lines nobody sent. Channel scope is now `ChatListener.GroupingChannels`,
an allow-list of conversational channels (`MentionSoundChannels`' universe
minus Tells/Echo, which groups still intentionally exclude), plus a
`name.Length > 0` guard in `ApplySenderGroupColor` mirroring the one
`ChatTwoStyleProvider.EvaluateCore` already had. `ChatTwoStyleProvider` keeps
referencing the same shared field, so both passes stay in sync. The rest of
this ADR's decisions (shared cooldown, mention-wins precedence, own-messages-
never-alert, sound cache) are unchanged.
