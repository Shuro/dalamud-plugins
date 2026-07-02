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

namespace GobchatEx.Core;

/// <summary>
/// Classification of a piece of chat message text, ported from Gobchat's
/// MessageSegmentType (minus WebLink, which has no native-chat equivalent).
/// </summary>
public enum SegmentType
{
    /// <summary>Unclassified text; rendered unchanged.</summary>
    Undefined,

    /// <summary>Quoted speech, e.g. "hello" or «hello».</summary>
    Say,

    /// <summary>Action text, e.g. *waves* or &lt;waves&gt;.</summary>
    Emote,

    /// <summary>Out-of-character text, e.g. ((brb)).</summary>
    Ooc,

    /// <summary>A configured mention trigger word.</summary>
    Mention,
}
