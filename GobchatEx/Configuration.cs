using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.Text;

namespace GobchatEx;

/// <summary>
/// Highlight style for one segment type. Colors are UIColor sheet row ids;
/// 0 means "do not recolor". Disabled types are not even parsed.
/// </summary>
[Serializable]
public class SegmentStyle
{
    public bool Enabled { get; set; } = true;
    public ushort Foreground { get; set; }
    public ushort Glow { get; set; }

    /// <summary>
    /// Copies all values from <paramref name="other"/> in place. In-place
    /// matters: the settings window's staged copy hands out references to
    /// these styles (e.g. to the color picker popup), which must stay live
    /// across re-initialisation.
    /// </summary>
    public void CopyFrom(SegmentStyle other)
    {
        Enabled = other.Enabled;
        Foreground = other.Foreground;
        Glow = other.Glow;
    }
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    // Bump this and add a migration block in your Plugin constructor when
    // you make backward-incompatible changes to the layout below.
    public int Version { get; set; } = 1;

    public bool IsConfigWindowMovable { get; set; } = true;

    // ------------------------------------------------------------------
    // RP highlighting. Default color rows are tuned visually in game;
    // delimiter rules themselves are fixed (Core/DefaultRules.cs).
    // ------------------------------------------------------------------
    public bool RpHighlightEnabled { get; set; } = true;

    public SegmentStyle SayStyle { get; set; } = new() { Foreground = 1 };
    public SegmentStyle EmoteStyle { get; set; } = new() { Foreground = 45 };
    public SegmentStyle OocStyle { get; set; } = new() { Foreground = 500 };
    public SegmentStyle MentionStyle { get; set; } = new() { Foreground = 48 };

    public static readonly IReadOnlyList<XivChatType> DefaultHighlightChannels =
    [
        XivChatType.Say,
        XivChatType.CustomEmote,
        XivChatType.Yell,
        XivChatType.Shout,
        XivChatType.Party,
        XivChatType.CrossParty,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.TellIncoming,
        XivChatType.TellOutgoing,
        // Not an RP channel, but /echo is the natural way to preview
        // formatting locally — and without it the plugin looks broken on
        // first try. Persisted configs keep their saved list; use the
        // "Reset to RP defaults" button to pick this up.
        XivChatType.Echo,
    ];

    public List<XivChatType> HighlightChannels { get; set; } = [.. DefaultHighlightChannels];

    /// <summary>Case-insensitive whole words that count as mentions.</summary>
    public List<string> MentionTriggers { get; set; } = [];

    public bool MentionSoundEnabled { get; set; }
    public int MentionSoundEffect { get; set; } = 2;    // <se.2>
    public int MentionSoundCooldownMs { get; set; } = 5000;
    public bool SuppressSoundFromSelf { get; set; } = true;

    /// <summary>
    /// Copies all user-editable settings from <paramref name="other"/> in
    /// place. Used by the settings window's staged-save model: stage into a
    /// mutable copy, apply back on Save. Must be in place — ChatListener
    /// holds a reference to the live instance. Version is deliberately
    /// excluded (migration-managed, never user-edited).
    /// Adding a property to this class? Add it here too.
    /// </summary>
    public void UpdateFrom(Configuration other)
    {
        IsConfigWindowMovable = other.IsConfigWindowMovable;
        RpHighlightEnabled = other.RpHighlightEnabled;
        SayStyle.CopyFrom(other.SayStyle);
        EmoteStyle.CopyFrom(other.EmoteStyle);
        OocStyle.CopyFrom(other.OocStyle);
        MentionStyle.CopyFrom(other.MentionStyle);
        HighlightChannels = [.. other.HighlightChannels];
        MentionTriggers = [.. other.MentionTriggers];
        MentionSoundEnabled = other.MentionSoundEnabled;
        MentionSoundEffect = other.MentionSoundEffect;
        MentionSoundCooldownMs = other.MentionSoundCooldownMs;
        SuppressSoundFromSelf = other.SuppressSoundFromSelf;
    }

    /// <summary>
    /// Persists the configuration to
    /// %AppData%\XIVLauncher\pluginConfigs\GobchatEx.json.
    /// Newtonsoft.Json with TypeNameHandling.Objects is used internally,
    /// so polymorphic fields round-trip correctly.
    /// </summary>
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
