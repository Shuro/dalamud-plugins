using System;
using System.Collections.Generic;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace GobchatEx.Config;

/// <summary>
/// RP highlighting settings, persisted to formatting.json. Default color rows
/// are tuned visually in game; delimiter rules themselves are fixed
/// (Core/DefaultRules.cs).
/// </summary>
[Serializable]
public class FormattingConfig
{
    public int Version { get; set; } = 1;

    public bool RpHighlightEnabled { get; set; } = true;

    // Defaults picked by Shuro: Say 549 = F3F3F3 soft white, Emote 500 = FF7B1A
    // orange, OOC 4 = 808080 grey, Mention 48 = AA81FF purple (values from the game's
    // UIColor sheet). The Formatting tab's reset buttons restore these constants.
    public const ushort DefaultSayForeground = 549;
    public const ushort DefaultEmoteForeground = 500;
    public const ushort DefaultOocForeground = 4;
    public const ushort DefaultMentionForeground = 48;

    public SegmentStyle SayStyle { get; set; } = new() { Foreground = DefaultSayForeground };
    public SegmentStyle EmoteStyle { get; set; } = new() { Foreground = DefaultEmoteForeground };
    public SegmentStyle OocStyle { get; set; } = new() { Foreground = DefaultOocForeground };
    public SegmentStyle MentionStyle { get; set; } = new() { Foreground = DefaultMentionForeground };

    public static readonly IReadOnlyList<XivChatType> DefaultHighlightChannels =
    [
        XivChatType.Say,
        XivChatType.CustomEmote,
        XivChatType.Party,
        XivChatType.CrossParty,
    ];

    // ObjectCreationHandling.Replace: without it, Json.NET's default Reuse behavior populates the
    // saved JSON array onto this property's non-empty initializer list via Add() instead of replacing
    // it, so every load-then-save cycle bakes the defaults back in on top of what was already
    // saved and the list grows unbounded. Needed on every list property with a non-empty default;
    // empty-default lists ([]) don't exhibit the bug.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<XivChatType> HighlightChannels { get; set; } = [.. DefaultHighlightChannels];
}
