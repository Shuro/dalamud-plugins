using System;
using GobchatEx.Localization;

namespace GobchatEx.Config;

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
        _ => LanguageOverride.None.Name(),
    };

    /// <summary>The culture code, or "" for "follow Dalamud" — also for any value a hand-edited
    /// general.json may hold that isn't a known member (Json.NET deserializes out-of-range enum
    /// integers without complaint), so a bad value can't throw during plugin construction.</summary>
    public static string Code(this LanguageOverride mode) => mode switch
    {
        LanguageOverride.English => "en",
        LanguageOverride.German => "de",
        _ => "",
    };
}
