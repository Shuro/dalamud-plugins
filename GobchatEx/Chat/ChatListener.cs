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
/// Subscribes to IChatGui.CheckMessageHandled and applies RP highlighting to
/// configured channels by rewriting the message's payload list. Subscribed
/// on the CheckMessageHandled pass rather than ChatMessage: Dalamud fires
/// ChatMessage first (a shared multicast event across every plugin, last
/// writer wins on message.Message) and CheckMessageHandled strictly after,
/// documented by Dalamud itself as the pass for "final modifications, like
/// translation or formatting" — exactly this plugin's job. That means
/// GobchatEx's formatting now always applies after any other plugin still
/// on the earlier ChatMessage pass (e.g. ChatAlerts), instead of losing an
/// arbitrary (plugin-load-order-dependent) race. Everything derived from
/// configuration (segmenter, channel set, style lookup) is rebuilt only on
/// <see cref="SettingsChanged"/>, never per message. CheckMessageHandled and
/// the config UI both run on the framework thread, so no locking is needed.
/// </summary>
public sealed class ChatListener : IDisposable
{
    private readonly Configuration _config;
    private readonly FriendGroupLookup _friendGroups;
    private readonly SoundPlayer _soundPlayer = new();

    private MessageSegmenter _segmenter = null!;
    private HashSet<XivChatType> _channels = null!;
    private Dictionary<SegmentType, (ushort Foreground, ushort Glow)> _styles = null!;
    private IReadOnlyList<GroupRule> _groupRules = [];
    private Dictionary<string, (ushort Foreground, ushort Glow)> _groupStyles = new();
    private bool _enabled;

    private static readonly MentionRules NoMentionRules = new([], [], [], FuzzyMatchLevel.Conservative);

    // Tells and Echo carry no real sender to group (Echo is local-only, matching IsFromSelf's own
    // reasoning); error messages are game system text. Mirrors the old app's channel exclusion.
    private static readonly HashSet<XivChatType> GroupingExcludedChannels =
        [XivChatType.TellIncoming, XivChatType.TellOutgoing, XivChatType.Echo, XivChatType.ErrorMessage];

    internal ChatListener(Configuration config, FriendGroupLookup friendGroups)
    {
        _config = config;
        _friendGroups = friendGroups;

        // A mid-session (re)load — plugin update or dev auto-reload — never fires Login, so seed
        // the friend-list snapshot now. Plugin construction is only framework-thread when the
        // manifest sets LoadSync (ours doesn't), and Refresh reads a game struct, so dispatch.
        if (Plugin.ClientState.IsLoggedIn)
            _ = Plugin.Framework.RunOnFrameworkThread(_friendGroups.Refresh);

        SettingsChanged();
        Plugin.ChatGui.CheckMessageHandled += OnChatMessage;
        Plugin.ClientState.Login += OnLogin;
        Plugin.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        Plugin.ClientState.Logout -= OnLogout;
        Plugin.ClientState.Login -= OnLogin;
        Plugin.ChatGui.CheckMessageHandled -= OnChatMessage;
    }

    private void OnLogin()
    {
        _friendGroups.Refresh();
        SettingsChanged();
    }

    private void OnLogout(int type, int code)
        => SettingsChanged();

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
        _segmenter = new MessageSegmenter(rules, wantMentions ? BuildMentionRules() : NoMentionRules);

        BuildGroupRules();
    }

    /// <summary>
    /// Custom groups first (so they take precedence over friend groups on the same sender, per
    /// GroupMatcher's first-match-wins order), then the 7 friend groups sorted by FfGroup defensively
    /// (they're always seeded 0..6 in order, but a hand-edited config shouldn't break precedence).
    /// </summary>
    private void BuildGroupRules()
    {
        var rules = new List<GroupRule>(_config.Groups.Count + _config.FriendGroups.Count);
        var styles = new Dictionary<string, (ushort Foreground, ushort Glow)>(rules.Capacity);

        foreach (var group in _config.Groups)
        {
            rules.Add(new GroupRule(group.Id, group.Active, FfGroup: null, group.Members));
            styles[group.Id] = (group.Foreground, group.Glow);
        }

        foreach (var group in _config.FriendGroups.OrderBy(g => g.FfGroup))
        {
            rules.Add(new GroupRule(group.Id, group.Active, group.FfGroup, Members: []));
            styles[group.Id] = (group.Foreground, group.Glow);
        }

        _groupRules = rules;
        _groupStyles = styles;
    }

    /// <summary>
    /// Global trigger words plus the currently logged-in character's resolved mention words (if that
    /// character is remembered and active), ported from the app's RecomputePlayerMentions /
    /// ApplyEffectiveMentions. Player-resolved whole words are unioned into the global list,
    /// case-insensitive de-duplicated; partial and fuzzy words are player-only.
    /// </summary>
    private MentionRules BuildMentionRules()
    {
        var wholeWords = new List<string>(_config.MentionTriggers);
        IReadOnlyList<string> partialWords = [];
        IReadOnlyList<string> fuzzyWords = [];
        var fuzzyLevel = FuzzyMatchLevel.Conservative;

        if (_config.PlayerMentionsEnabled && Plugin.PlayerState.IsLoaded)
        {
            var playerName = Plugin.PlayerState.CharacterName;
            var character = _config.Characters.FirstOrDefault(c =>
                c.Active && string.Equals(c.Name, playerName, StringComparison.OrdinalIgnoreCase));

            if (character != null)
            {
                var resolved = PlayerMentionResolver.ResolveWords(
                    character.Name,
                    character.MatchFullName,
                    character.MatchFirstName,
                    character.MatchLastName,
                    character.MatchFirstNamePartial,
                    character.MatchLastNamePartial,
                    character.MatchMiqote,
                    character.CustomWords);

                foreach (var word in resolved.WholeWords)
                    if (!wholeWords.Any(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
                        wholeWords.Add(word);

                partialWords = resolved.PartialWords;

                if (character.MatchFuzzy)
                {
                    fuzzyWords = PlayerMentionResolver.FuzzyCandidates(resolved);
                    fuzzyLevel = character.FuzzyLevel;
                }
            }
        }

        return new MentionRules(wholeWords, partialWords, fuzzyWords, fuzzyLevel);
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
        if (_enabled && _channels.Contains(message.LogKind))
            ApplyBodyHighlighting(message);

        ApplySenderGroupColor(message);
    }

    private void ApplyBodyHighlighting(IHandleableChatMessage message)
    {
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

    /// <summary>
    /// Recolors the sender name when it belongs to a matching custom or friend group. Independent of
    /// <see cref="_enabled"/>/<see cref="_channels"/> (the RP-highlighting master switch and channel
    /// filter) — group coloring is its own feature and applies wherever a sender exists.
    /// </summary>
    private void ApplySenderGroupColor(IHandleableChatMessage message)
    {
        if (GroupingExcludedChannels.Contains(message.LogKind))
            return;

        SenderIdentity.Resolve(message.Sender, out var name, out var world);
        var friendGroupIndex = _friendGroups.TryGetFriendGroupIndex(name, world, out var index) ? index : (int?)null;

        var groupId = GroupMatcher.FindGroup(name, world, friendGroupIndex, _groupRules);
        if (groupId == null
            || !_groupStyles.TryGetValue(groupId, out var style)
            || (style.Foreground == 0 && style.Glow == 0))
            return;

        var payloads = message.Sender.Payloads;
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

        var rewritten = PayloadRewriter.RewriteUniform(payloads, runIndices!, runTexts, style);
        message.Sender = new SeString(rewritten);
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
    /// Heuristic own-message check: TellOutgoing and Echo are unconditionally self (a Tell you sent,
    /// or a local-only /echo print that carries no sender at all — Dalamud's own self-test identifies
    /// Echo messages purely by Message text for the same reason); otherwise the sender text must
    /// contain the local player's name (tolerant of party number prefixes and cross-world suffixes).
    /// Only the sound is suppressed for own messages, never the highlighting.
    /// </summary>
    private static bool IsFromSelf(IHandleableChatMessage message)
    {
        if (message.LogKind is XivChatType.TellOutgoing or XivChatType.Echo)
            return true;

        var localName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue;
        return !string.IsNullOrEmpty(localName)
            && message.Sender.TextValue.Contains(localName, StringComparison.Ordinal);
    }
}
