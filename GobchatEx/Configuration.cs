using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using Newtonsoft.Json;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx;

/// <summary>
/// Highlight style for one segment type. Colors are UIColor sheet row ids;
/// 0 means "do not recolor". Disabled types are not even parsed.
/// </summary>
[Serializable]
public class SegmentStyle
{
    public bool Enabled { get; set; } = true;
    public ushort Foreground { get; set; }
    public ushort Glow { get; set; }

    /// <summary>
    /// Copies all values from <paramref name="other"/> in place. In-place
    /// matters: the settings window's staged copy hands out references to
    /// these styles (e.g. to the color picker popup), which must stay live
    /// across re-initialisation.
    /// </summary>
    public void CopyFrom(SegmentStyle other)
    {
        Enabled = other.Enabled;
        Foreground = other.Foreground;
        Glow = other.Glow;
    }
}

/// <summary>
/// Per-character player-mention settings: which name parts count as mentions
/// (whole-word and/or partial-substring), Miqo'te apostrophe-segment
/// matching, typo-tolerant fuzzy matching, and custom extra words. One
/// entry per character remembered via login (Milestone 1); mirrors the
/// standalone app's per-character mention template.
/// </summary>
[Serializable]
public class CharacterMentionSettings
{
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool MatchFullName { get; set; } = true;
    public bool MatchFirstName { get; set; } = true;
    public bool MatchLastName { get; set; } = true;
    public bool MatchFirstNamePartial { get; set; }
    public bool MatchLastNamePartial { get; set; }
    public bool MatchMiqote { get; set; }
    public bool MatchFuzzy { get; set; }
    public FuzzyMatchLevel FuzzyLevel { get; set; } = FuzzyMatchLevel.Conservative;
    public List<string> CustomWords { get; set; } = [];

    public CharacterMentionSettings Clone() => new()
    {
        Name = Name,
        Active = Active,
        MatchFullName = MatchFullName,
        MatchFirstName = MatchFirstName,
        MatchLastName = MatchLastName,
        MatchFirstNamePartial = MatchFirstNamePartial,
        MatchLastNamePartial = MatchLastNamePartial,
        MatchMiqote = MatchMiqote,
        MatchFuzzy = MatchFuzzy,
        FuzzyLevel = FuzzyLevel,
        CustomWords = [.. CustomWords],
    };
}

/// <summary>
/// One player group (Milestone 2): either a custom group matched by a <see cref="Members"/> list, or
/// one of the game's seven fixed friend-list display groups matched by <see cref="FfGroup"/>
/// (0=Star..6=Club). Sender-name recoloring uses <see cref="Foreground"/>/<see cref="Glow"/> the same
/// way segment styles do; 0 means "do not recolor".
/// </summary>
[Serializable]
public class PlayerGroup
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

    public ushort Foreground { get; set; }
    public ushort Glow { get; set; }

    public PlayerGroup Clone() => new()
    {
        Id = Id,
        Name = Name,
        Active = Active,
        FfGroup = FfGroup,
        Members = [.. Members], // GroupMember is an immutable record; a shallow copy is a deep copy
        Foreground = Foreground,
        Glow = Glow,
    };
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    // Bump this and add a migration block in your Plugin constructor when
    // you make backward-incompatible changes to the layout below.
    public int Version { get; set; } = 1;

    public bool IsConfigWindowMovable { get; set; } = true;

    /// <summary>UI language for the plugin's own settings window (not in-game chat text).</summary>
    public LanguageOverride LanguageOverride { get; set; } = LanguageOverride.None;

    // ------------------------------------------------------------------
    // RP highlighting. Default color rows are tuned visually in game;
    // delimiter rules themselves are fixed (Core/DefaultRules.cs).
    // ------------------------------------------------------------------
    public bool RpHighlightEnabled { get; set; } = true;

    public SegmentStyle SayStyle { get; set; } = new() { Foreground = 1 };
    public SegmentStyle EmoteStyle { get; set; } = new() { Foreground = 45 };
    public SegmentStyle OocStyle { get; set; } = new() { Foreground = 500 };
    public SegmentStyle MentionStyle { get; set; } = new() { Foreground = 48 };

    public static readonly IReadOnlyList<XivChatType> DefaultHighlightChannels =
    [
        XivChatType.Say,
        XivChatType.CustomEmote,
        XivChatType.Party,
        XivChatType.CrossParty,
    ];

    // ObjectCreationHandling.Replace: without it, Json.NET's default Reuse behavior populates the
    // saved JSON array onto this property's non-empty initializer list via Add() instead of replacing
    // it, so every load-then-save cycle bakes the 11 defaults back in on top of what was already
    // saved and the list grows unbounded. Only needed here because this is the one list property with
    // a non-empty default; empty-default lists ([]) don't exhibit the bug.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<XivChatType> HighlightChannels { get; set; } = [.. DefaultHighlightChannels];

    /// <summary>Case-insensitive whole words that count as mentions.</summary>
    public List<string> MentionTriggers { get; set; } = [];

    /// <summary>
    /// Master switch for player-name mentions (Milestone 1). Characters are
    /// auto-learned at login but added inactive; this only gates whether
    /// any of them are matched at all.
    /// </summary>
    public bool PlayerMentionsEnabled { get; set; } = true;

    /// <summary>Remembered characters and their per-character mention settings.</summary>
    public List<CharacterMentionSettings> Characters { get; set; } = [];

    public bool MentionSoundEnabled { get; set; }
    public int MentionSoundEffect { get; set; } = 2;    // <se.2>
    public int MentionSoundCooldownMs { get; set; } = 5000;
    public bool SuppressSoundFromSelf { get; set; } = true;

    /// <summary>Custom player groups (Milestone 2), reorderable, matched by player-name trigger lists.</summary>
    public List<PlayerGroup> Groups { get; set; } = [];

    /// <summary>
    /// The game's seven friend-list display groups, matched by sender FfGroup index (0=Star..6=Club).
    /// Always exactly 7 entries, seeded once by <see cref="CreateDefaultFriendGroups"/> when the plugin
    /// migrates to config version 3 (see Plugin()'s constructor); the Groups tab can toggle Active and
    /// pick a color per row but never add, remove, or rename them.
    /// </summary>
    public List<PlayerGroup> FriendGroups { get; set; } = [];

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

    // ------------------------------------------------------------------
    // Range filter (Milestone 3): fade chat from far-away players into
    // darkened color steps, suppress it beyond the cut-off. Distances are
    // linear in-game yalms; defaults mirror the app's default_profile.json
    // (cutoff 24, fadeout 16, channels Say/Emote/AnimatedEmote).
    // ------------------------------------------------------------------
    public bool RangeFilterEnabled { get; set; }
    public float RangeFilterCutOff { get; set; } = 24f;
    public float RangeFilterFadeOut { get; set; } = 16f;

    /// <summary>Mentions bypass the range filter, so a far-away player calling your name still shows.</summary>
    public bool RangeFilterMentionsIgnoreRange { get; set; } = true;

    public static readonly IReadOnlyList<XivChatType> DefaultRangeFilterChannels =
    [
        XivChatType.Say,
        XivChatType.CustomEmote,
        XivChatType.StandardEmote,
    ];

    // Replace, not Reuse: same Json.NET default-list-append bug as HighlightChannels above.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<XivChatType> RangeFilterChannels { get; set; } = [.. DefaultRangeFilterChannels];

    /// <summary>
    /// Copies all user-editable settings from <paramref name="other"/> in
    /// place. Used by the settings window's staged-save model: stage into a
    /// mutable copy, apply back on Save. Must be in place — ChatListener
    /// holds a reference to the live instance. Version is deliberately
    /// excluded (migration-managed, never user-edited).
    /// Adding a property to this class? Add it here too.
    /// </summary>
    public void UpdateFrom(Configuration other)
    {
        IsConfigWindowMovable = other.IsConfigWindowMovable;
        LanguageOverride = other.LanguageOverride;
        RpHighlightEnabled = other.RpHighlightEnabled;
        SayStyle.CopyFrom(other.SayStyle);
        EmoteStyle.CopyFrom(other.EmoteStyle);
        OocStyle.CopyFrom(other.OocStyle);
        MentionStyle.CopyFrom(other.MentionStyle);
        HighlightChannels = [.. other.HighlightChannels];
        MentionTriggers = [.. other.MentionTriggers];
        PlayerMentionsEnabled = other.PlayerMentionsEnabled;
        Characters = [.. other.Characters.Select(c => c.Clone())];
        MentionSoundEnabled = other.MentionSoundEnabled;
        MentionSoundEffect = other.MentionSoundEffect;
        MentionSoundCooldownMs = other.MentionSoundCooldownMs;
        SuppressSoundFromSelf = other.SuppressSoundFromSelf;
        Groups = [.. other.Groups.Select(g => g.Clone())];
        FriendGroups = [.. other.FriendGroups.Select(g => g.Clone())];
        RangeFilterEnabled = other.RangeFilterEnabled;
        RangeFilterCutOff = other.RangeFilterCutOff;
        RangeFilterFadeOut = other.RangeFilterFadeOut;
        RangeFilterMentionsIgnoreRange = other.RangeFilterMentionsIgnoreRange;
        RangeFilterChannels = [.. other.RangeFilterChannels];
    }

    /// <summary>
    /// Persists the configuration to {ConfigDirectory}\config.json, e.g.
    /// %AppData%\XIVLauncher\pluginConfigs\GobchatEx\config.json. Plain
    /// Newtonsoft.Json (no TypeNameHandling): Load() always deserializes into
    /// the concrete Configuration type, so no polymorphic type metadata needs
    /// to round-trip. Writes via temp-file-then-move so a crash mid-write can't
    /// leave a truncated config (Dalamud's own SavePluginConfig has SQLite
    /// journaling we lose by bypassing it — this is a cheap partial mitigation).
    /// A failed write (locked file, permission error, disk full) is logged and
    /// swallowed rather than thrown — callers include the login handler in
    /// ChatListener, which must not crash plugin load over a transient I/O error.
    /// </summary>
    public void Save()
    {
        var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "config.json");

        try
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Plugin.Log.Error(ex, "Failed to save {Path}.", path);
        }
    }

    /// <summary>
    /// Loads the configuration from {ConfigDirectory}\config.json. Returns a
    /// fresh default instance on first run (file doesn't exist yet) or if the
    /// file can't be read or parsed (corrupt/foreign JSON, locked file,
    /// permission error) — logged, never thrown.
    /// </summary>
    public static Configuration Load()
    {
        var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "config.json");
        if (!File.Exists(path))
            return new Configuration();

        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Configuration>(json) ?? new Configuration();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Plugin.Log.Error(ex, "Failed to load {Path}, starting with default configuration.", path);
            return new Configuration();
        }
    }
}

[Serializable]
public enum LanguageOverride
{
    None,
    English,
    German,
}

public static class LanguageOverrideExt
{
    public static string Name(this LanguageOverride mode) => mode switch
    {
        LanguageOverride.None => Loc.Get("Language_Option_UseDalamudDefault"),
        LanguageOverride.English => "English",
        LanguageOverride.German => "Deutsch",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static string Code(this LanguageOverride mode) => mode switch
    {
        LanguageOverride.None => "",
        LanguageOverride.English => "en",
        LanguageOverride.German => "de",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
