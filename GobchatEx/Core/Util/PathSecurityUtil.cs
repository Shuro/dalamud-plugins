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

using System;
using System.IO;
using System.Security;

namespace GobchatEx.Core.Util;

/// <summary>
/// Helpers for keeping file-system access inside an allowed root directory, ported from the app.
/// Used by the chat logger so a hand-edited relative path from chatlog.json resolves inside the
/// plugin config directory and cannot escape into arbitrary locations via <c>..</c> traversal.
/// Comparisons are case-insensitive (the plugin is Windows-only).
/// </summary>
public static class PathSecurityUtil
{
    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="root"/> itself or a path nested
    /// underneath it, after canonicalising both with <see cref="Path.GetFullPath(string)"/>.
    /// A shared textual prefix (e.g. <c>root</c> vs <c>rootSibling</c>) is NOT containment.
    /// </summary>
    public static bool IsContainedIn(string root, string candidate)
    {
        if (string.IsNullOrEmpty(root))
            throw new ArgumentNullException(nameof(root));
        if (string.IsNullOrEmpty(candidate))
            throw new ArgumentNullException(nameof(candidate));

        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);

        if (string.Equals(fullCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves <paramref name="pathMaybeRelative"/> against <paramref name="root"/> (relative
    /// paths are combined with the root; absolute paths are used as given) and returns the
    /// canonical full path, but only if the result stays within the root. Throws
    /// <see cref="UnauthorizedAccessException"/> when the path would escape the root.
    /// </summary>
    public static string ResolveWithin(string root, string pathMaybeRelative)
    {
        if (string.IsNullOrEmpty(root))
            throw new ArgumentNullException(nameof(root));
        if (string.IsNullOrEmpty(pathMaybeRelative))
            throw new ArgumentNullException(nameof(pathMaybeRelative));

        var combined = Path.IsPathRooted(pathMaybeRelative)
            ? pathMaybeRelative
            : Path.Combine(root, pathMaybeRelative);

        var fullPath = Path.GetFullPath(combined);
        if (!IsContainedIn(root, fullPath))
            throw new UnauthorizedAccessException(
                $"Path '{pathMaybeRelative}' resolves outside the allowed directory.");

        return fullPath;
    }

    /// <summary>
    /// Resolves the chat logger's configured log folder. There is no default: empty means unconfigured and
    /// resolves to an empty string, which keeps logging disabled. The folder picker stores
    /// absolute paths (allowed anywhere); a hand-edited relative path must resolve inside the
    /// config directory (<see cref="ResolveWithin"/>) — escaping or malformed paths are flagged invalid
    /// and also resolve to empty.
    /// </summary>
    public static string ResolveLogFolder(string configured, string configDir, out bool invalid)
    {
        invalid = false;
        var trimmed = configured.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        try
        {
            // IsPathFullyQualified, not IsPathRooted: drive-relative ("C:logs") and root-relative
            // ("\logs") strings count as rooted but GetFullPath resolves them against the current
            // working directory — they must go through the containment check, not the
            // trusted-picker branch, or a hand-edited config escapes the sandbox.
            return Path.IsPathFullyQualified(trimmed)
                ? Path.GetFullPath(trimmed)
                : PathSecurityUtil.ResolveWithin(configDir, trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
            or UnauthorizedAccessException or SecurityException)
        {
            invalid = true;
            return string.Empty;
        }
    }
}
