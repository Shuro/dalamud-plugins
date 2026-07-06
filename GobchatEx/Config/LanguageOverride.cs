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
