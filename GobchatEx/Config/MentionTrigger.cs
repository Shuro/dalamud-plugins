using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Trims, rejects empty input and case-insensitive duplicates, then appends (no color override —
    /// set from the swatches afterward). The one rule the Mentions tab and "/gex mention add" share,
    /// so both agree on what counts as a duplicate. True when added.
    /// </summary>
    public static bool TryAddUnique(List<MentionTrigger> list, string input)
    {
        var value = input.Trim();
        if (value.Length == 0 || list.Any(x => string.Equals(x.Word, value, StringComparison.OrdinalIgnoreCase)))
            return false;

        list.Add(new MentionTrigger { Word = value });
        return true;
    }
}
