using System.Globalization;
using System.Resources;

namespace GobchatEx.Localization;

/// <summary>
/// Thin wrapper over a ResourceManager for the Settings UI's own strings —
/// window labels, tooltips, buttons. This plugin never translates in-game
/// chat text (it only recolors it in place), so this is UI-chrome-only
/// localization. A missing key returns the key itself (visible-but-not-
/// crashing) rather than throwing; a key missing from the German satellite
/// but present in the neutral resx falls back to English automatically via
/// ResourceManager's built-in fallback chain.
/// </summary>
internal static class Loc
{
    private static readonly ResourceManager Manager =
        new("GobchatEx.Resources.Language", typeof(Loc).Assembly);

    public static CultureInfo Culture { get; set; } = CultureInfo.CurrentUICulture;

    public static string Get(string key) => Resolve(Manager, Culture, key);

    /// <summary>
    /// Pure resolution step, split out so it's unit-testable against a
    /// throwaway resx fixture without needing the real compiled-in
    /// Resources/Language.resx (tests/GobchatEx.Core.Tests has no reference
    /// to GobchatEx.dll — see ADR 0002 in the test csproj).
    /// </summary>
    internal static string Resolve(ResourceManager manager, CultureInfo culture, string key)
    {
        try
        {
            return manager.GetString(key, culture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }
}
