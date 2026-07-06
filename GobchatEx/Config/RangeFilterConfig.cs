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
/// Includes the filter's Chat 2 rendering switches (Milestone 3.5).
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

    public static readonly IReadOnlyList<XivChatType> DefaultRangeFilterChannels =
    [
        XivChatType.Say,
        XivChatType.CustomEmote,
        XivChatType.StandardEmote,
    ];

    // Replace, not Reuse: same Json.NET default-list-append bug as FormattingConfig.HighlightChannels.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<XivChatType> RangeFilterChannels { get; set; } = [.. DefaultRangeFilterChannels];

    // ------------------------------------------------------------------
    // Chat 2 rendering (Milestone 3.5): applied through Chat 2's message
    // styling IPC when it is connected; nothing here affects the native
    // log (which keeps the darkened-step "lite" dimming above). Group
    // backgrounds live on PlayerGroup.ChatTwoBackground.
    // ------------------------------------------------------------------

    /// <summary>Range filter renders true per-message alpha in Chat 2 instead of darkened steps.</summary>
    public bool RangeFilterChatTwoFade { get; set; } = true;

    /// <summary>Range filter hides beyond-cut-off messages in Chat 2 (render-only; they stay in Chat 2's history).</summary>
    public bool RangeFilterChatTwoHide { get; set; } = true;
}
