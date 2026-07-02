using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>Built-in chat sound effects, the &lt;se.1&gt;–&lt;se.16&gt; macro sounds.</summary>
public static class GameSound
{
    public const int Min = 1;
    public const int Max = 16;

    public static string Name(int effect) => string.Format(Loc.Get("Sound_EffectName"), effect);
}
