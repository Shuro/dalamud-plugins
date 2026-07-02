using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Subscribes to IChatGui.ChatMessage and applies RP highlighting to
/// configured channels by rewriting the message's payload list. Everything
/// derived from configuration (segmenter, channel set, style lookup) is
/// rebuilt only on <see cref="SettingsChanged"/>, never per message.
/// ChatMessage and the config UI both run on the framework thread, so no
/// locking is needed.
/// </summary>
public sealed class ChatListener : IDisposable
{
    private readonly Configuration _config;
    private readonly SoundPlayer _soundPlayer = new();

    private MessageSegmenter _segmenter = null!;
    private HashSet<XivChatType> _channels = null!;
    private Dictionary<SegmentType, (ushort Foreground, ushort Glow)> _styles = null!;
    private bool _enabled;

    public ChatListener(Configuration config)
    {
        _config = config;
        SettingsChanged();
        Plugin.ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
        => Plugin.ChatGui.ChatMessage -= OnChatMessage;

    /// <summary>Call after any configuration change (and Save()).</summary>
    public void SettingsChanged()
    {
        _enabled = _config.RpHighlightEnabled;
        _channels = [.. _config.HighlightChannels];
        _styles = new Dictionary<SegmentType, (ushort, ushort)>
        {
            [SegmentType.Say] = StyleTuple(_config.SayStyle),
            [SegmentType.Emote] = StyleTuple(_config.EmoteStyle),
            [SegmentType.Ooc] = StyleTuple(_config.OocStyle),
            [SegmentType.Mention] = StyleTuple(_config.MentionStyle),
        };

        var rules = DefaultRules.All.Where(rule => StyleFor(rule.Type).Enabled).ToList();

        // Mention detection also runs highlight-disabled when the sound alert
        // needs it; the rewriter then renders those spans plain via (0, 0).
        var wantMentions = _config.MentionStyle.Enabled || _config.MentionSoundEnabled;
        _segmenter = new MessageSegmenter(rules, wantMentions ? _config.MentionTriggers : []);
    }

    private static (ushort Foreground, ushort Glow) StyleTuple(SegmentStyle style)
        => style.Enabled ? (style.Foreground, style.Glow) : ((ushort)0, (ushort)0);

    private SegmentStyle StyleFor(SegmentType type)
        => type switch
        {
            SegmentType.Say => _config.SayStyle,
            SegmentType.Emote => _config.EmoteStyle,
            SegmentType.Ooc => _config.OocStyle,
            _ => _config.MentionStyle,
        };

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!_enabled || !_channels.Contains(message.LogKind))
            return;

        var payloads = message.Message.Payloads;

        // Extract the text runs. Like ChatAlerts, every TextPayload counts —
        // including link display text; our balanced on/off pairs nest inside
        // pre-colored regions and pop back correctly.
        List<int>? runIndices = null;
        List<string>? runTexts = null;
        for (var i = 0; i < payloads.Count; ++i)
        {
            if (payloads[i] is not TextPayload { Text.Length: > 0 } textPayload)
                continue;

            runIndices ??= [];
            runTexts ??= [];
            runIndices.Add(i);
            runTexts.Add(textPayload.Text);
        }

        if (runTexts == null)
            return;

        var result = _segmenter.Segment(runTexts);
        if (result == null)
            return;

        var rewritten = PayloadRewriter.Rewrite(payloads, runIndices!, runTexts, result.RunSpans, _styles);
        message.Message = new SeString(rewritten);

        if (result.HasMention)
            TryPlayMentionSound(message);
    }

    private void TryPlayMentionSound(IHandleableChatMessage message)
    {
        if (!_config.MentionSoundEnabled)
            return;
        if (_config.SuppressSoundFromSelf && IsFromSelf(message))
            return;

        _soundPlayer.TryPlay(_config.MentionSoundEffect, _config.MentionSoundCooldownMs);
    }

    /// <summary>
    /// Heuristic own-message check: TellOutgoing is exact; otherwise the
    /// sender text must contain the local player's name (tolerant of party
    /// number prefixes and cross-world suffixes). Only the sound is
    /// suppressed for own messages, never the highlighting.
    /// </summary>
    private static bool IsFromSelf(IHandleableChatMessage message)
    {
        if (message.LogKind == XivChatType.TellOutgoing)
            return true;

        var localName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue;
        return !string.IsNullOrEmpty(localName)
            && message.Sender.TextValue.Contains(localName, StringComparison.Ordinal);
    }
}
