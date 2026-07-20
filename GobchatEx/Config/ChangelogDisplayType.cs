using System;
using GobchatEx.Localization;

namespace GobchatEx.Config;

[Serializable]
public enum ChangelogDisplayType
{
    New,
    HighlightOnly,
    Never,
}

public static class ChangelogDisplayTypeExt
{
    public static string Name(this ChangelogDisplayType mode) => mode switch
    {
        ChangelogDisplayType.New => Loc.Get("Changelog_Display_New"),
        ChangelogDisplayType.HighlightOnly => Loc.Get("Changelog_Display_HighlightOnly"),
        ChangelogDisplayType.Never => Loc.Get("Changelog_Display_Never"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
