using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GobchatEx.Config;

/// <summary>Player-group settings (Milestone 2), persisted to groups.json.</summary>
[Serializable]
public class GroupsConfig
{
    public int Version { get; set; } = 1;

    /// <summary>Master switch for the whole groups feature: custom groups and friend groups alike.</summary>
    public bool GroupsEnabled { get; set; } = true;

    /// <summary>Custom player groups (Milestone 2), reorderable, matched by player-name trigger lists.</summary>
    public List<PlayerGroup> Groups { get; set; } = [];

    /// <summary>
    /// The game's seven friend-list display groups, matched by sender FfGroup index (0=Star..6=Club).
    /// Always exactly 7 entries, seeded by the initializer; the Groups tab can toggle Active and
    /// pick a color per row but never add, remove, or rename them.
    /// </summary>
    // ObjectCreationHandling.Replace: mandatory on this non-empty-default list — see the same
    // attribute on FormattingConfig.HighlightChannels for the Json.NET Reuse-append bug it fixes.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<PlayerGroup> FriendGroups { get; set; } = CreateDefaultFriendGroups();

    /// <summary>Stable ids and <c>FfGroup</c> indices mirror FFXIVClientStructs' DisplayGroup enum (Star=1..Club=7, offset by -1).</summary>
    public static List<PlayerGroup> CreateDefaultFriendGroups() =>
    [
        new() { Id = "ffgroup-0", Name = "Star", FfGroup = 0 },
        new() { Id = "ffgroup-1", Name = "Circle", FfGroup = 1 },
        new() { Id = "ffgroup-2", Name = "Triangle", FfGroup = 2 },
        new() { Id = "ffgroup-3", Name = "Diamond", FfGroup = 3 },
        new() { Id = "ffgroup-4", Name = "Heart", FfGroup = 4 },
        new() { Id = "ffgroup-5", Name = "Spade", FfGroup = 5 },
        new() { Id = "ffgroup-6", Name = "Club", FfGroup = 6 },
    ];
}
