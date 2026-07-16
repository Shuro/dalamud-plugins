/*******************************************************************************
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
/// Resolves a mention span's effective color: a per-word override falls back, component by
/// component, to the default mention style — an override with only a foreground keeps the
/// default glow, and vice versa, matching the plugin-wide "0 = unset" color-picker convention.
/// <paramref name="styleId"/> 0, or out of range for <paramref name="styles"/>, means no override
/// at all: the default is returned untouched. An empty <paramref name="styles"/> table therefore
/// degrades every override to the default in one place — how the Mention style's own Enabled
/// toggle turns off per-word colors along with everything else.
/// </summary>
public static class MentionStyleResolver
{
    public static (uint Foreground, uint Glow) Resolve(
        int styleId,
        IReadOnlyList<(uint Foreground, uint Glow)> styles,
        uint defaultForeground,
        uint defaultGlow)
    {
        if (styleId <= 0 || styleId > styles.Count)
            return (defaultForeground, defaultGlow);

        var (foreground, glow) = styles[styleId - 1];
        return (foreground != 0 ? foreground : defaultForeground, glow != 0 ? glow : defaultGlow);
    }
}
