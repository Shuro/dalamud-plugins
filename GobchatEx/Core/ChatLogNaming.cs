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
using System.Globalization;
using System.Text;

namespace GobchatEx.Core;

/// <summary>
/// File and folder naming for the chat logger:
/// character names are reduced to file-system-safe tokens (FFXIV names contain apostrophes), and
/// each session file is named with an invariant, minute-precision timestamp so archives sort
/// chronologically regardless of the user's locale.
/// </summary>
public static class ChatLogNaming
{
    /// <summary>
    /// Reduces a character name to a filename-safe token: letters/digits kept, whitespace and
    /// hyphens become a single '-', everything else (apostrophes, punctuation) dropped. E.g.
    /// "J'ohn Gobchat" -> "John-Gobchat".
    /// </summary>
    public static string SanitizeForFileName(string? name) => Sanitize(name, whitespace: '-');

    /// <summary>
    /// Like <see cref="SanitizeForFileName"/> but keeps spaces, so the result reads as a folder name
    /// ("J'ohn Gobchat" -> "John Gobchat"). Hyphens are kept in both forms, so a hyphenated name
    /// sanitizes the same way in its folder and its file names.
    /// </summary>
    public static string SanitizeForFolderName(string? name) => Sanitize(name, whitespace: ' ');

    /// <summary>
    /// Letters/digits and hyphens are kept, whitespace becomes <paramref name="whitespace"/>,
    /// everything else (apostrophes, punctuation, invalid path chars) is dropped. Separator runs
    /// collapse to their first separator; no leading or trailing separator.
    /// </summary>
    private static string Sanitize(string? name, char whitespace)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            char toAppend;
            if (char.IsLetterOrDigit(ch) || ch == '-')
                toAppend = ch;
            else if (char.IsWhiteSpace(ch))
                toAppend = whitespace;
            else
                continue;

            if (IsSeparator(toAppend) && (sb.Length == 0 || IsSeparator(sb[^1])))
                continue;
            sb.Append(toAppend);
        }

        while (sb.Length > 0 && IsSeparator(sb[^1]))
            sb.Length--;
        return sb.ToString();
    }

    private static bool IsSeparator(char c) => c is '-' or ' ';

    /// <summary>
    /// Builds the per-session log filename: <c>chatlog_{yyyy-MM-dd_HH-mm}[_{Character}].log</c>,
    /// the character suffix omitted when the name sanitizes away entirely.
    /// </summary>
    public static string BuildFileName(DateTimeOffset now, string? characterName)
    {
        var timestamp = now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);
        var character = SanitizeForFileName(characterName);
        return character.Length == 0
            ? $"chatlog_{timestamp}.log"
            : $"chatlog_{timestamp}_{character}.log";
    }
}
