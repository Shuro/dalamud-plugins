using System;
using System.Collections.Generic;
using Dalamud.Game.Text;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>One recorded mention: plain-string snapshots taken at message time — the message
/// object is pooled and only valid during the chat callback (same capture rule as
/// <see cref="ChatLogger"/>). <paramref name="MentionSpans"/> are the mention matches within
/// <paramref name="Message"/> (offsets valid for that exact string), and
/// <paramref name="Matches"/> is their distinct matched texts pre-joined for the table column —
/// both empty when the match couldn't be reproduced on the original text (see
/// <see cref="ChatListener"/>.RecordMention).</summary>
internal sealed record MentionHistoryEntry(
    DateTimeOffset Timestamp,
    XivChatType Channel,
    string SenderName,
    string? SenderWorld,
    string Message,
    IReadOnlyList<SegmentSpan> MentionSpans,
    string Matches)
{
    /// <summary>Monotonic per-session id, assigned by <see cref="MentionHistory.Add"/> — the
    /// stable ImGui id for the entry's row. Loop indices shift when the capacity eviction
    /// drops the oldest entry, which would silently re-bind an open sender context popup to
    /// whatever entry slid into that index.</summary>
    public int Sequence { get; init; }
}

/// <summary>
/// The last <see cref="Capacity"/> mentions (Milestone 7), in-memory only — nothing is
/// persisted, deliberately: recent mentions are a session aid, and storing other players' chat
/// on disk stays exclusive to the opt-in chat logger. Written from the chat pass and read by
/// <see cref="Windows.MentionHistoryWindow"/>; both run on the framework thread, so no locking
/// is needed. Entries are oldest-first — the window iterates in reverse for newest-first.
/// </summary>
internal sealed class MentionHistory
{
    private const int Capacity = 50;

    private readonly List<MentionHistoryEntry> _entries = new(Capacity);
    private int _nextSequence;

    public IReadOnlyList<MentionHistoryEntry> Entries => _entries;

    public void Add(MentionHistoryEntry entry)
    {
        if (_entries.Count == Capacity)
            _entries.RemoveAt(0);
        _entries.Add(entry with { Sequence = _nextSequence++ });
    }

    public void Clear() => _entries.Clear();
}
