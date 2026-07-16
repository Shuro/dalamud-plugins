using System;

namespace GobchatEx.Config;

/// <summary>
/// A mention word (a global trigger or a character's custom word) with an optional per-word
/// color/glow override. 0 means "unset" for either component — the match falls back to the
/// default mention style at render time (see <see cref="Core.MentionStyleResolver"/>).
/// </summary>
[Serializable]
public class MentionTrigger
{
    public string Word { get; set; } = string.Empty;
    public uint Foreground { get; set; }
    public uint Glow { get; set; }
}
