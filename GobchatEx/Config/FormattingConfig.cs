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

    // The app's "autodetect emote" rule (behaviour.chat.autodetectEmoteInSay/-InParty): a quoted
    // span in a Say/Party message flags all remaining unmarked text as Emote. App-parity defaults.
    public bool DetectEmoteInSay { get; set; } = true;
    public bool DetectEmoteInParty { get; set; }

    // Defaults picked by Shuro, packed RGBA (0xRRGGBBAA): Say = F8F8F8 soft white, Emote =
    // FF8000 the game's own emote-channel orange, OOC = 808080 grey, Mention = AA81FF purple.
    // The Formatting tab's reset buttons restore these constants.
    public const uint DefaultSayForeground = 0xF8F8F8FF;
    public const uint DefaultEmoteForeground = 0xFF8000FF;
    public const uint DefaultOocForeground = 0x808080FF;
    public const uint DefaultMentionForeground = 0xAA81FFFF;

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
