using System;
using System.IO;
using Newtonsoft.Json;

namespace GobchatEx.Config;

/// <summary>
/// Root of the plugin configuration: one section object per feature area, each
/// persisted to its own JSON file in {ConfigDirectory}, e.g.
/// %AppData%\XIVLauncher\pluginConfigs\GobchatEx\rangefilter.json. Section
/// instances are created once by <see cref="Load"/> and never replaced
/// afterward — consumers (settings tabs, chat pipeline) hold direct references
/// to them and mutate properties in place.
/// </summary>
public sealed class Configuration
{
    public GeneralConfig General { get; init; } = new();
    public FormattingConfig Formatting { get; init; } = new();
    public MentionsConfig Mentions { get; init; } = new();
    public GroupsConfig Groups { get; init; } = new();
    public RangeFilterConfig RangeFilter { get; init; } = new();
    public TabsConfig Tabs { get; init; } = new();

    /// <summary>
    /// The fixed section→file mapping that <see cref="Save"/> and the settings
    /// window's per-section change detection iterate over.
    /// </summary>
    internal (string FileName, object Section)[] Sections =>
    [
        ("general.json", General),
        ("formatting.json", Formatting),
        ("mentions.json", Mentions),
        ("groups.json", Groups),
        ("rangefilter.json", RangeFilter),
        ("tabs.json", Tabs),
    ];

    /// <summary>
    /// The one serializer definition for both disk writes and the settings
    /// window's change-detection snapshots — the two must use identical
    /// settings to compare equal. Plain Newtonsoft.Json (no TypeNameHandling):
    /// sections always deserialize into their concrete types, so no
    /// polymorphic type metadata needs to round-trip.
    /// </summary>
    internal static string Serialize(object section) =>
        JsonConvert.SerializeObject(section, Newtonsoft.Json.Formatting.Indented);

    /// <summary>Persists every section to its own file.</summary>
    public void Save()
    {
        foreach (var (fileName, section) in Sections)
            SaveSection(fileName, Serialize(section));
    }

    /// <summary>
    /// Writes one section file, JSON already serialized — lets the settings
    /// window reuse the snapshots it built for change detection and write only
    /// the sections that actually changed. Writes via temp-file-then-move so a
    /// crash mid-write can't leave a truncated file. A failed write (locked
    /// file, permission error, disk full) is logged and swallowed rather than
    /// thrown — callers include the login handler in ChatListener, which must
    /// not crash plugin load over a transient I/O error.
    /// </summary>
    internal static void SaveSection(string fileName, string json)
    {
        var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, fileName);

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
    /// Loads every section from its own file. A missing file means first run
    /// for that section — defaults are written back immediately so the config
    /// directory always shows the full state. An unreadable or corrupt file is
    /// logged and that section alone falls back to defaults (not written back,
    /// so the broken file stays inspectable until the next save); the other
    /// sections are unaffected.
    /// </summary>
    public static Configuration Load() => new()
    {
        General = LoadSection<GeneralConfig>("general.json"),
        Formatting = LoadSection<FormattingConfig>("formatting.json"),
        Mentions = LoadSection<MentionsConfig>("mentions.json"),
        Groups = LoadSection<GroupsConfig>("groups.json"),
        RangeFilter = LoadSection<RangeFilterConfig>("rangefilter.json"),
        Tabs = LoadSection<TabsConfig>("tabs.json"),
    };

    private static T LoadSection<T>(string fileName) where T : new()
    {
        var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, fileName);
        if (!File.Exists(path))
        {
            var section = new T();
            SaveSection(fileName, Serialize(section));
            return section;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Plugin.Log.Error(ex, "Failed to load {Path}, starting that section with defaults.", path);
            return new T();
        }
    }
}
