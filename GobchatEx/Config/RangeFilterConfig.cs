using System;
using System.Collections.Generic;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace GobchatEx.Config;

/// <summary>
/// Range filter settings (Milestone 3), persisted to rangefilter.json: fade
/// chat from far-away players into darkened color steps, suppress it beyond
/// the cut-off. Distances are linear in-game yalms; defaults mirror the app's
/// default_profile.json (cutoff 24, fadeout 16, channels Say/Emote/AnimatedEmote).
/// </summary>
[Serializable]
public class RangeFilterConfig
{
    public int Version { get; set; } = 1;

    public bool RangeFilterEnabled { get; set; }
    public float RangeFilterCutOff { get; set; } = 24f;
    public float RangeFilterFadeOut { get; set; } = 16f;

    /// <summary>Mentions bypass the range filter, so a far-away player calling your name still shows.</summary>
    public bool RangeFilterMentionsIgnoreRange { get; set; } = true;

    // Replace, not Reuse: same Json.NET default-list-append bug as FormattingConfig.HighlightChannels.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<XivChatType> RangeFilterChannels { get; set; } =
    [
        XivChatType.Say,
        XivChatType.CustomEmote,
        XivChatType.StandardEmote,
    ];
}
