# 1. Render RP highlighting by rewriting SeString payloads in the native chat log

Date: 2026-07-01

## Status

Accepted

## Context

The original GobchatEx desktop app rendered roleplay highlighting (say quotes,
emotes, OOC, mentions) in its own WebView2 overlay window, which required a
separate process, per-pixel-alpha compositing, and its own chat log, tabs and
scrollback. As a Dalamud plugin we have a second option, proven by the
ChatAlerts plugin: intercept `IChatGui.ChatMessage` and rewrite the message's
SeString payload list in place, inserting `UIForegroundPayload` /
`UIGlowPayload` pairs around the text to recolor, so the game's own chat
window renders the highlighting.

## Decision

Version 1 renders RP highlighting exclusively in the native chat log via
SeString payload rewriting. No overlay window is built. The segmentation
engine (`GobchatEx/Core/`) is kept UI-agnostic so a future overlay could
consume the same spans.

## Consequences

- Users keep their existing chat tabs, filters and fonts; no new window to
  position or configure.
- Colors are limited to the game's `UIColor` sheet rows (~500 preset colors)
  instead of arbitrary RGB; configuration stores row IDs and the UI offers a
  swatch picker.
- We coexist with other chat-rewriting plugins: unknown payloads are copied
  through verbatim and every color payload is emitted as a balanced on/off
  pair, so foreign payloads merely split text runs.
- Message-history recoloring is out of reach: only messages arriving while
  the plugin is loaded are highlighted.
