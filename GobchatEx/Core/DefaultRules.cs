/*******************************************************************************
 * Copyright (C) 2019-2025 MarbleBag
 * Copyright (C) 2026 Shuro
 *
 * This program is free software: you can redistribute it and/or modify it under
 * the terms of the GNU Affero General Public License as published by the Free
 * Software Foundation, version 3.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>
 *
 * SPDX-License-Identifier: AGPL-3.0-only
 *******************************************************************************/

using System.Collections.Generic;

namespace GobchatEx.Core;

/// <summary>
/// The fixed delimiter rules, ported from Gobchat's default_profile.json.
/// List order is processing order and therefore precedence: earlier rules
/// claim text first (OOC beats Emote beats Say).
/// <para>
/// Quote styles that share an opening token live in ONE rule with every accepted closer. As
/// separate rules (the app's layout), the first one claimed the shared opener and — never
/// finding its own closer — marked everything to the end of the message, so „…” or «…» dragged
/// the rest of the line (including a following *emote*) into Say.
/// </para>
/// </summary>
public static class DefaultRules
{
    public static readonly IReadOnlyList<TokenRule> All =
    [
        new(SegmentType.Ooc,   ["(("], ["))"]),
        new(SegmentType.Emote, ["*"], ["*"]),
        new(SegmentType.Emote, ["<"], [">"]),
        new(SegmentType.Say,   ["\""], ["\""]),          // straight quotes
        new(SegmentType.Say,   ["„"], ["“", "”"]),  // „…“ (German) and „…”
        new(SegmentType.Say,   ["“"], ["”"]),  // “…”
        new(SegmentType.Say,   ["»", "«"], ["«", "»"]),  // »…« and «…»
    ];
}
