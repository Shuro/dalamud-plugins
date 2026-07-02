---
name: consolidate-memory
description: Reflective audit-consolidate-prune pass over this environment's native file-based memory (MEMORY.md index plus per-topic files with metadata.type user/feedback/project/reference). Use when memory has grown stale or duplicated, the MEMORY.md index is approaching 200 lines / 25KB, or when explicitly asked to clean up, audit, or consolidate memory.
metadata:
  source: https://github.com/Tihlyn/RedMoon-Cappuccino/blob/main/.github/copilot/skills/consolidate-memory/SKILL.md
  license: AGPL-3.0-or-later
  adapted: true
---

# Memory Consolidation

> Ported from [RedMoon-Cappuccino](https://github.com/Tihlyn/RedMoon-Cappuccino) (AGPL-3.0-or-later) and adapted to operate on this environment's actual memory schema rather than a generic placeholder.

You're doing a reflective pass over what's been recorded about the user and this project. The goal: a future session should be able to orient quickly — who the user is, what they're working on, how they like things done — without re-asking.

Your system prompt's auto-memory section defines the exact directory, file format, and memory types for this environment. Follow it — do not invent a different schema. As of this writing that means: a memory directory containing a `MEMORY.md` index (one-line-per-entry, truncated after 200 lines) plus per-topic `.md` files, each with `name`, `description`, and `metadata: { type: user | feedback | project | reference }` frontmatter.

## Phase 1 — Take stock (audit)

- List the memory directory and read `MEMORY.md` in full.
- Read the frontmatter and skim the body of every topic file the index points to.
- Flag candidates for the next phase:
  - Two or more files describing overlapping facts, people, or preferences.
  - `type: project` files describing work that has since finished or been superseded.
  - Index lines over ~150 characters, or the index total approaching the 200-line / 25KB budget.
  - Topic files whose content has drifted from their own `description` field.
  - `type: reference` files pointing at something that looks version-stale (a tool, API, or path that may have moved on).

## Phase 2 — Consolidate

**Separate the durable from the dated.** `type: user` and `type: feedback` entries — preferences, working style, corrections the user gave and why — are durable; keep and sharpen them. `type: project` entries are dated; if the work is done or the deadline has passed, retire the file or fold the lasting takeaway into a durable `type: user`/`type: reference` file instead.

**Merge overlaps.** If two files describe the same person, project, or preference, combine into one, keep the richer file's path, and preserve the most accurate `metadata.type`.

**Fix time references.** Convert "next week", "this quarter", "by Friday" to absolute dates so they stay readable later.

**Don't silently trust stale references.** For `type: reference` entries that look version- or path-dependent, note the doubt in the file rather than deleting or repeating it uncritically — this environment only knows what's on disk, not whether the external system it points to has changed.

**Drop what's easy to re-find.** If a memory just restates something derivable from the current code or git history, cut it — this mirrors the "what NOT to save" guidance already in the auto-memory instructions.

**Confirm before anything destructive.** Before merging or retiring a file, state which files are affected and why, and get the user's go-ahead. This memory store may be shared project context, not a personal single-user scratchpad, so treat merges and retirements as reversible-with-review, not silent.

## Phase 3 — Tidy the index

Update `MEMORY.md` so it stays under 200 lines and ~25KB. One line per entry, under ~150 chars: `- [Title](file.md) — one-line hook`.

- Remove pointers to retired memories.
- Shorten any line carrying detail that belongs in the topic file instead.
- Add anything newly important that surfaced during the audit.

Finish with a short summary: how many files were touched, merged, or retired, and the index line-count before/after.
