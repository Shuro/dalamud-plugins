using System;
using System.Collections.Generic;
using GobchatEx.Core;

namespace GobchatEx.Config;

/// <summary>
/// One player group (Milestone 2): either a custom group matched by a <see cref="Members"/> list, or
/// one of the game's seven fixed friend-list display groups matched by <see cref="FfGroup"/>
/// (0=Star..6=Club). Sender-name recoloring uses <see cref="Foreground"/>/<see cref="Glow"/> the same
/// way segment styles do — packed RGBA (0xRRGGBBAA), same format as <see cref="ChatTwoBackground"/>,
/// rendered via a raw SeString Color/EdgeColor macro; 0 means "do not recolor".
/// </summary>
[Serializable]
public class PlayerGroup : IAlertSoundSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public int? FfGroup { get; set; }

    /// <summary>
    /// Stored exactly as entered (original casing) — GroupMatcher folds case at match time, never at
    /// rest, so the saved config stays readable/professional-looking instead of forcing everything to
    /// lowercase.
    /// </summary>
    public List<GroupMember> Members { get; set; } = [];

    public uint Foreground { get; set; }
    public uint Glow { get; set; }

    /// <summary>
    /// Per-message background color rendered in Chat 2 (0xRRGGBBAA, 0 = none). Chat 2-only: the
    /// native log can't draw backgrounds, so this only renders through Chat 2's styling IPC
    /// (Milestone 3.5) and its settings UI is disabled while that IPC isn't connected.
    /// </summary>
    public uint ChatTwoBackground { get; set; }

    /// <summary>
    /// Play an alert sound when a message from a group member arrives (Milestone 6). The sound
    /// itself is the <see cref="IAlertSoundSettings"/> quartet below; policy — one shared
    /// cooldown (<see cref="GroupsConfig.GroupSoundCooldownMs"/>), the mention alert winning on
    /// overlap, own messages never alerting — is ADR 0005.
    /// </summary>
    public bool SoundEnabled { get; set; }

    public bool SoundUseCustomFile { get; set; }
    public int SoundEffect { get; set; } = 2;    // <se.2>, matching MentionsConfig's default
    public string SoundFilePath { get; set; } = string.Empty;
    public float SoundVolume { get; set; } = 1f;
}
