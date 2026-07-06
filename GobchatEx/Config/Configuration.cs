using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Configuration;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace GobchatEx.Config;

[Serializable]
public class Configuration : IPluginConfiguration
{
    // Bump this and add a migration block in your Plugin constructor when
    // you make backward-incompatible changes to the layout below.
    public int Version { get; set; } = 1;

    /// <summary>UI language for the plugin's own settings window (not in-game chat text).</summary>
    public LanguageOverride LanguageOverride { get; set; } = LanguageOverride.None;

    // ------------------------------------------------------------------
    // RP highlighting. Default color rows are tuned visually in game;
    // delimiter rules themselves are fixed (Core/DefaultRules.cs).
    // ------------------------------------------------------------------
    public bool RpHighlightEnabled { get; set; } = true;

    // Defaults picked by Shuro (config v5): Say 549 = F3F3F3 soft white, Emote 500 = FF7B1A
    // orange, OOC 4 = 808080 grey, Mention 48 = AA81FF purple (values from the game's UIColor
    // sheet). The v3→v5 migration in Plugin() moves saved values still on the old defaults;
    // the Formatting tab's reset buttons restore these constants.
    public const ushort DefaultSayForeground = 549;
    public const ushort DefaultEmoteForeground = 500;
    public const ushort DefaultOocForeground = 4;
    public const ushort DefaultMentionForeground = 48;

    public SegmentStyle SayStyle { get; set; } = new() { Foreground = DefaultSayForeground };
    public SegmentStyle EmoteStyle { get; set; } = new() { Foreground = DefaultEmoteForeground };
    public SegmentStyle OocStyle { get; set; } = new() { Foreground = DefaultOocForeground };
    public SegmentStyle MentionStyle { get; set; } = new() { Foreground = DefaultMentionForeground };

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

    /// <summary>
    /// Master switch for the whole mentions feature: global trigger words and player-name
    /// mentions alike. Nested under this is <see cref="PlayerMentionsEnabled"/>, which only
    /// gates the player-name-derived subset.
    /// </summary>
    public bool MentionsEnabled { get; set; } = true;

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

    /// <summary>Master switch for the whole groups feature: custom groups and friend groups alike.</summary>
    public bool GroupsEnabled { get; set; } = true;

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

    // ------------------------------------------------------------------
    // Chat 2 styling (Milestone 3.5): rendered through Chat 2's message
    // styling IPC when it is connected; nothing here affects the native
    // log (which keeps the darkened-step "lite" dimming above). Group
    // backgrounds live on PlayerGroup.ChatTwoBackground.
    // ------------------------------------------------------------------

    /// <summary>Range filter renders true per-message alpha in Chat 2 instead of darkened steps.</summary>
    public bool RangeFilterChatTwoFade { get; set; } = true;

    /// <summary>Range filter hides beyond-cut-off messages in Chat 2 (render-only; they stay in Chat 2's history).</summary>
    public bool RangeFilterChatTwoHide { get; set; } = true;

    /// <summary>
    /// Per-Chat 2-tab styling suppression flags (1 = no backgrounds, 2 = no fade, 4 = no hide),
    /// keyed by Chat 2's persistent tab identifier; tabs without an entry get everything.
    /// </summary>
    public Dictionary<Guid, int> ChatTwoTabPolicies { get; set; } = [];

    /// <summary>
    /// Serializes the full configuration to the JSON persisted by
    /// <see cref="Save()"/>. Also used by the settings window's instant-apply
    /// change detection, which compares snapshots of this string — both must
    /// use identical serializer settings, hence the shared method.
    /// </summary>
    internal string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

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
    public void Save() => Save(ToJson());

    /// <summary>
    /// <see cref="Save()"/> with the JSON already serialized — lets the
    /// settings window reuse the snapshot it built for change detection
    /// instead of serializing twice.
    /// </summary>
    internal void Save(string json)
    {
        var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "config.json");

        try
        {
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
