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
using System.Collections.Generic;
using System.IO;
using GobchatEx.Core.Util;

namespace GobchatEx.Core;

/// <summary>One pending disk write: append <see cref="Lines"/> to <see cref="FilePath"/>.
/// <see cref="IsNewFile"/> marks the first write of a session file (for diagnostics).</summary>
public sealed record ChatLogWrite(string FilePath, IReadOnlyList<string> Lines, bool IsNewFile);

/// <summary>
/// Session state machine for the chat logger (Milestone 5), ported from the app's ChatLoggerBase
/// semantics: one file per login/character switch, the file is only named once the first loggable
/// line is flushed (an idle session never creates an empty file), and lines enqueued while logged
/// out are dropped. Does no I/O itself — <see cref="DequeueWrite"/> hands the Dalamud layer a write
/// instruction — which keeps every rule here unit-testable with an injected clock. Not thread-safe:
/// the plugin drives it from the framework thread only.
/// </summary>
public sealed class ChatLogSession(Func<DateTimeOffset> clock)
{
    private readonly Queue<string> pending = new();
    private string logFolder = string.Empty;
    private bool useCharacterFolders;

    /// <summary>The logged-in character, or null while logged out (lines are dropped then).</summary>
    public string? CharacterName { get; private set; }

    /// <summary>The file the session is appending to; null until the first flush names one.</summary>
    public string? CurrentFilePath { get; private set; }

    public int PendingCount => pending.Count;

    /// <summary>
    /// Applies the folder settings. A no-op when nothing changed — the settings window notifies on
    /// ANY section edit, and an unrelated change must not rotate the log file. When the folder or
    /// the per-character toggle did change, the current file is closed and the next line starts a
    /// fresh file in the new location. Callers flush first so pending lines land in the old file.
    /// </summary>
    public void Configure(string logFolder, bool useCharacterFolders)
    {
        if (this.logFolder == logFolder && this.useCharacterFolders == useCharacterFolders)
            return;

        this.logFolder = logFolder;
        this.useCharacterFolders = useCharacterFolders;
        CurrentFilePath = null;
    }

    /// <summary>
    /// Sets the logged-in character (null/whitespace = logged out). A changed character closes the
    /// current file so the next line opens a new one. Callers flush first so pending lines land in
    /// the old character's file.
    /// </summary>
    public void SetCharacter(string? characterName)
    {
        var normalized = string.IsNullOrWhiteSpace(characterName) ? null : characterName.Trim();
        if (string.Equals(CharacterName, normalized, StringComparison.Ordinal))
            return;

        CharacterName = normalized;
        CurrentFilePath = null;
    }

    /// <summary>
    /// Adopts the file a previous plugin lifetime was appending to (reload/update resume) so the
    /// next line continues it instead of naming a fresh file. Guarded rather than trusted — the
    /// resume marker on disk may predate a folder or character change: the path only sticks while
    /// a character is set and it still lies inside the configured log folder; otherwise normal
    /// lazy naming applies. Returns whether the file was adopted.
    /// </summary>
    public bool TryResumeFile(string? filePath)
    {
        if (CharacterName == null || logFolder.Length == 0 || string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            if (!PathSecurityUtil.IsContainedIn(logFolder, filePath))
                return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
            or System.Security.SecurityException)
        {
            return false; // malformed marker path — fall back to lazy naming
        }

        CurrentFilePath = filePath;
        return true;
    }

    /// <summary>Queues one formatted line; dropped while logged out.</summary>
    public void Enqueue(string line)
    {
        if (CharacterName == null)
            return;
        pending.Enqueue(line);
    }

    /// <summary>
    /// Drains the queue into one write instruction, naming the session file on the first call
    /// (lazy — this is the moment the file starts existing). Null when there is nothing to write.
    /// </summary>
    public ChatLogWrite? DequeueWrite()
    {
        if (pending.Count == 0)
            return null;

        var isNewFile = CurrentFilePath == null;
        if (isNewFile)
        {
            var folder = logFolder;
            if (useCharacterFolders)
            {
                var characterFolder = ChatLogNaming.SanitizeForFolderName(CharacterName);
                if (characterFolder.Length > 0)
                    folder = Path.Combine(logFolder, characterFolder);
            }

            CurrentFilePath = Path.Combine(folder, ChatLogNaming.BuildFileName(clock(), CharacterName));
        }

        var lines = new List<string>(pending.Count);
        while (pending.Count > 0)
            lines.Add(pending.Dequeue());

        return new ChatLogWrite(CurrentFilePath!, lines, isNewFile);
    }
}
