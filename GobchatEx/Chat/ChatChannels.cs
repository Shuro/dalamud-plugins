using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Config;
using Dalamud.Game.Text;

namespace GobchatEx.Chat;

/// <summary>One selectable channel in a settings grid: its label key, the channel, an optional help-marker key.</summary>
internal readonly record struct ChannelChoice(string LabelKey, XivChatType Type, string? HelpKey = null);

/// <summary>
/// The single source for every channel set the plugin uses: the settings grids (Formatting, Logs,
/// Range) and the chat pipeline's scopes derive from the lists here, so they can't drift apart.
/// </summary>
internal static class ChatChannels
{
    public static readonly ChannelChoice[] Main =
    [
        new("Formatting_Channel_Say", XivChatType.Say),
        new("Formatting_Channel_Emote", XivChatType.CustomEmote),
        new("Formatting_Channel_StandardEmote", XivChatType.StandardEmote),
        new("Formatting_Channel_Yell", XivChatType.Yell),
        new("Formatting_Channel_Shout", XivChatType.Shout),
        new("Formatting_Channel_Party", XivChatType.Party),
        new("Formatting_Channel_CrossParty", XivChatType.CrossParty),
        new("Formatting_Channel_Alliance", XivChatType.Alliance),
        new("Formatting_Channel_FreeCompany", XivChatType.FreeCompany),
        new("Formatting_Channel_TellIn", XivChatType.TellIncoming),
        new("Formatting_Channel_TellOut", XivChatType.TellOutgoing),
        new("Formatting_Channel_NoviceNetwork", XivChatType.NoviceNetwork),
        new("Formatting_Channel_Echo", XivChatType.Echo),
    ];

    public static readonly ChannelChoice[] Linkshells =
    [
        new("Formatting_Channel_Linkshell1", XivChatType.Ls1),
        new("Formatting_Channel_Linkshell2", XivChatType.Ls2),
        new("Formatting_Channel_Linkshell3", XivChatType.Ls3),
        new("Formatting_Channel_Linkshell4", XivChatType.Ls4),
        new("Formatting_Channel_Linkshell5", XivChatType.Ls5),
        new("Formatting_Channel_Linkshell6", XivChatType.Ls6),
        new("Formatting_Channel_Linkshell7", XivChatType.Ls7),
        new("Formatting_Channel_Linkshell8", XivChatType.Ls8),
    ];

    public static readonly ChannelChoice[] CrossworldLinkshells =
    [
        new("Formatting_Channel_Cwls1", XivChatType.CrossLinkShell1),
        new("Formatting_Channel_Cwls2", XivChatType.CrossLinkShell2),
        new("Formatting_Channel_Cwls3", XivChatType.CrossLinkShell3),
        new("Formatting_Channel_Cwls4", XivChatType.CrossLinkShell4),
        new("Formatting_Channel_Cwls5", XivChatType.CrossLinkShell5),
        new("Formatting_Channel_Cwls6", XivChatType.CrossLinkShell6),
        new("Formatting_Channel_Cwls7", XivChatType.CrossLinkShell7),
        new("Formatting_Channel_Cwls8", XivChatType.CrossLinkShell8),
    ];

    /// <summary>
    /// Every conversational channel (the three grids above). The mention probe runs only on these:
    /// CheckMessageHandled also fires for combat, loot and system LogKinds, whose lines routinely
    /// contain player names (false mention dings) and would pay a segmentation scan per line.
    /// </summary>
    public static readonly HashSet<XivChatType> Conversational =
        [.. Main.Concat(Linkshells).Concat(CrossworldLinkshells).Select(c => c.Type)];

    /// <summary>
    /// Where group coloring, backgrounds and sounds apply: an allow-list, so senderless
    /// system/notification types and any future chat type fail closed. Tells and Echo carry no
    /// real sender to group (Echo is local-only).
    /// </summary>
    public static readonly HashSet<XivChatType> Grouping =
        [.. Conversational.Except([XivChatType.TellIncoming, XivChatType.TellOutgoing, XivChatType.Echo])];

    /// <summary>
    /// The range filter's proximity channels — range-filtering a server-wide channel (party, FC,
    /// linkshells) would hide messages based on where the sender happens to stand. Say/Emote carry
    /// the engine-limit help marker: the game only delivers them up to ~20 yalms.
    /// </summary>
    public static readonly ChannelChoice[] Range =
    [
        new("Formatting_Channel_Say", XivChatType.Say, "Range_EngineLimit_Tooltip"),
        new("Formatting_Channel_Emote", XivChatType.CustomEmote, "Range_EngineLimit_Tooltip"),
        new("Formatting_Channel_StandardEmote", XivChatType.StandardEmote),
        new("Formatting_Channel_Yell", XivChatType.Yell),
        new("Formatting_Channel_Shout", XivChatType.Shout),
    ];

    /// <summary>
    /// Each <see cref="Range"/> channel's own Log Text Color option (Character Configuration →
    /// Log Text Color) — what a faded, otherwise uncolored message on that channel darkens from.
    /// </summary>
    public static readonly Dictionary<XivChatType, UiConfigOption> RangeColorOptions = new()
    {
        [XivChatType.Say] = UiConfigOption.ColorSay,
        [XivChatType.CustomEmote] = UiConfigOption.ColorEmoteUser,
        [XivChatType.StandardEmote] = UiConfigOption.ColorEmote,
        [XivChatType.Yell] = UiConfigOption.ColorYell,
        [XivChatType.Shout] = UiConfigOption.ColorShout,
    };

    /// <summary>
    /// TellOutgoing and Echo are unconditionally self: a tell you sent, or a local-only /echo print
    /// that carries no sender at all.
    /// </summary>
    public static bool IsSelfChannel(XivChatType type)
        => type is XivChatType.TellOutgoing or XivChatType.Echo;
}
