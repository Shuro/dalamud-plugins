using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Utility;
using GobchatEx.Config;
using GobchatEx.Core;
using Lumina.Text.ReadOnly;

namespace GobchatEx.Chat;

/// <summary>
/// Subscribes to IChatGui.CheckMessageHandled and applies RP highlighting, group coloring, mention
/// alerts and the range fade by rewriting the message's payload list. CheckMessageHandled rather
/// than ChatMessage: Dalamud fires ChatMessage first (a shared multicast, last writer wins on
/// message.Message) and CheckMessageHandled strictly after, documented as the pass for "final
/// modifications, like translation or formatting" — so GobchatEx's formatting always applies after
/// plugins on the earlier pass (e.g. ChatAlerts) instead of losing a load-order race. Everything
/// derived from configuration is rebuilt only in <see cref="SettingsChanged"/>, never per message.
/// CheckMessageHandled and the config UI both run on the framework thread, so no locking is needed.
/// </summary>
public sealed class ChatListener : IDisposable
{
    // Used only when a channel has no Log Text Color option or the game config read fails —
    // RangeFadeStep already gates on the range channels, which all have one.
    internal const uint FallbackFadeColor = 0x808080FF;

    private readonly Configuration _config;
    private readonly FriendGroupLookup _friendGroups;
    private readonly SoundPlayer _soundPlayer;
    private readonly MentionHistory _mentionHistory;

    private MessageSegmenter _segmenter = null!;
    private HashSet<XivChatType> _channels = null!;
    private Dictionary<SegmentType, (uint Foreground, uint Glow)> _styles = null!;

    // Per-word mention color-override table (StyleId - 1 indexes it), resolved via
    // MentionStyleResolver. No Mention span exists while the Mention style is off (the overlay is
    // skipped), so the table never renders then; it still feeds MentionHistory's colors.
    private IReadOnlyList<(uint Foreground, uint Glow)> _mentionWordStyles = [];
    private IReadOnlyList<GroupRule> _groupRules = [];
    private Dictionary<string, PlayerGroup> _groupsById = new();
    private bool _enabled;
    private bool _overlayMentions;
    private bool _detectEmoteInSay;
    private bool _detectEmoteInParty;
    private bool _rangeEnabled;
    private HashSet<XivChatType> _rangeChannels = [];
    private Dictionary<XivChatType, uint> _chatTwoChannelColors = [];
    private bool _chatMessageErrorLogged;

    /// <summary>The message's sender, resolved once per message.</summary>
    /// <param name="Name">Clean name (no party-number prefix when a PlayerPayload was present).</param>
    /// <param name="World">World, or null when the sender carried none (e.g. own posts).</param>
    /// <param name="IsSelf">Heuristic own-message flag (<see cref="SelfSender"/>).</param>
    private readonly record struct Sender(string Name, string? World, bool IsSelf);

    /// <summary>
    /// The channel's own configured chat color (config-storage 0xRRGGBBAA, not yet dimmed) — what a
    /// plain, unformatted message on that channel fades from, so Yell stays yellowish and Shout
    /// orange-red. Prefers Chat 2's customized color for that channel when one is on file (an
    /// explicit Color macro permanently overrides Chat 2's per-channel rendering, so vanilla's color
    /// would mismatch every non-faded message for a Chat 2 user who customized it), then the vanilla
    /// Log Text Color, then <see cref="FallbackFadeColor"/>.
    /// </summary>
    internal uint ResolveChannelColor(XivChatType channel) => ResolveChannelColorWithSource(channel).Color;

    /// <summary>
    /// <see cref="ResolveChannelColor"/> plus which tier won, for the Debug page's range pane.
    /// <paramref name="liveChatTwoRead"/> re-reads Chat 2's config file instead of the cache
    /// refreshed in <see cref="SettingsChanged"/> — never per message, only from a Debug button.
    /// </summary>
    internal (uint Color, string Source) ResolveChannelColorWithSource(XivChatType channel, bool liveChatTwoRead = false)
    {
        var chatTwoColors = liveChatTwoRead ? ChatTwoChannelColors.Read() : _chatTwoChannelColors;

        if (chatTwoColors.TryGetValue(channel, out var chatTwoColor) && chatTwoColor != 0)
            return (chatTwoColor, "Chat 2");

        if (ChatChannels.RangeColorOptions.TryGetValue(channel, out var option)
            && Plugin.GameConfig.TryGet(option, out uint raw)
            && RgbaColor.FromGameConfigColor(raw) is { } color)
            return (color, "vanilla");

        return (FallbackFadeColor, "fallback");
    }

    /// <summary>
    /// Resolves the fade step for 0 &lt;= visibility &lt; 100 (callers handle 100 = fully visible
    /// themselves). 0 maps to the darkest step; the open interval splits into the partial steps
    /// 1..n-2 via <see cref="RangeFade.FadeStep"/>. Step 0 (undimmed) is never produced.
    /// </summary>
    internal static int ResolveFadeStep(int visibility) => visibility == 0
        ? UiColorDimmer.StepCount - 1
        : 1 + RangeFade.FadeStep(visibility, UiColorDimmer.StepCount - 2);

    internal ChatListener(Configuration config, FriendGroupLookup friendGroups, SoundPlayer soundPlayer,
        MentionHistory mentionHistory)
    {
        _config = config;
        _friendGroups = friendGroups;
        _soundPlayer = soundPlayer;
        _mentionHistory = mentionHistory;

        // A mid-session (re)load — plugin update or dev auto-reload — never fires Login, so seed
        // the friend-list snapshot now. Plugin construction is only framework-thread when the
        // manifest sets LoadSync (ours doesn't), and Refresh reads a game struct, so dispatch.
        if (Plugin.ClientState.IsLoggedIn)
        {
            _ = Plugin.Framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    _friendGroups.Refresh();
                }
                catch (Exception ex)
                {
                    // Fire-and-forget dispatch: without this, a throw here (ClientStructs read,
                    // Excel lookup) would vanish into the discarded task and friend-group
                    // coloring would stay silently empty for the whole session.
                    Plugin.Log.Error(ex, "Initial friend-list refresh failed; friend-group coloring stays empty until the next refresh");
                }
            });
        }

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

    /// <summary>Rebuilds everything derived from configuration; called by the ConfigCommitter cascade and on login/logout.</summary>
    public void SettingsChanged()
    {
        var formatting = _config.Formatting;
        _chatMessageErrorLogged = false;
        _enabled = formatting.RpHighlightEnabled;
        _channels = [.. formatting.HighlightChannels];
        _styles = new Dictionary<SegmentType, (uint, uint)>
        {
            [SegmentType.Say] = StyleTuple(formatting.SayStyle),
            [SegmentType.Emote] = StyleTuple(formatting.EmoteStyle),
            [SegmentType.Ooc] = StyleTuple(formatting.OocStyle),
            [SegmentType.Mention] = StyleTuple(formatting.MentionStyle),
        };

        var rules = DefaultRules.All.Where(rule => StyleFor(rule.Type).Enabled).ToList();

        // Gated on the Emote style, mirroring DefaultTypeFor's "a disabled style never produces
        // that SegmentType" rule — otherwise leftovers would be typed Emote and render plain via
        // the (0, 0) tuple instead of falling back to the Say channel default. The Say side needs
        // no gate: with SayStyle disabled no Say token rule is built, so detection is inert.
        _detectEmoteInSay = formatting.DetectEmoteInSay && formatting.EmoteStyle.Enabled;
        _detectEmoteInParty = formatting.DetectEmoteInParty && formatting.EmoteStyle.Enabled;

        _rangeEnabled = _config.RangeFilter.RangeFilterEnabled;
        _rangeChannels = [.. _config.RangeFilter.RangeFilterChannels];
        _chatTwoChannelColors = ChatTwoChannelColors.Read();

        // Mention detection runs whenever the mentions feature is on, independent of the Mention
        // style and the sound: the mention history records every detected mention, and the range
        // filter's bypass needs it too. With the Mention style off the overlay is skipped, so a
        // detected name keeps its Say/Emote color instead of rendering plain.
        var mentionRules = MentionRulesFactory.Build(_config.Mentions);
        _overlayMentions = formatting.MentionStyle.Enabled;
        _segmenter = new MessageSegmenter(rules, mentionRules);
        _mentionWordStyles = mentionRules.Styles ?? [];

        // Rule ordering (the precedence invariant) lives in GroupRuleBuilder, shared with the Chat 2
        // provider.
        var groupsEnabled = _config.Groups.GroupsEnabled;
        _groupRules = groupsEnabled ? GroupRuleBuilder.Build(_config.Groups, snapshotMembers: false) : [];
        _groupsById = groupsEnabled ? _config.Groups.AllGroups
            .GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.Last()) // last wins on a hand-edited duplicate id
            : new();
    }

    private static (uint Foreground, uint Glow) StyleTuple(SegmentStyle style)
        => style.Enabled ? (style.Foreground, style.Glow) : (0u, 0u);

    private SegmentStyle StyleFor(SegmentType type)
        => type switch
        {
            SegmentType.Say => _config.Formatting.SayStyle,
            SegmentType.Emote => _config.Formatting.EmoteStyle,
            SegmentType.Ooc => _config.Formatting.OocStyle,
            _ => _config.Formatting.MentionStyle,
        };

    /// <summary>
    /// A /say or /emote line is implicitly said/emoted even without quote or asterisk delimiters
    /// (Dalamud resolves the /s and /em aliases to the same channel). Only when the corresponding
    /// style is enabled — a disabled style never produces that <see cref="SegmentType"/>.
    /// </summary>
    private SegmentType DefaultTypeFor(XivChatType channel) => channel switch
    {
        XivChatType.Say when StyleFor(SegmentType.Say).Enabled => SegmentType.Say,
        XivChatType.CustomEmote when StyleFor(SegmentType.Emote).Enabled => SegmentType.Emote,
        _ => SegmentType.Undefined,
    };

    /// <summary>
    /// Whether the emote autodetection applies on this channel: a quoted span then flags all
    /// remaining unmarked text as Emote (see <see cref="MessageSegmenter.Segment"/>). Party and
    /// cross-world party are paired like everywhere else.
    /// </summary>
    private bool DetectEmoteFor(XivChatType channel) => channel switch
    {
        XivChatType.Say => _detectEmoteInSay,
        XivChatType.Party or XivChatType.CrossParty => _detectEmoteInParty,
        _ => false,
    };

    /// <summary>
    /// Top-level guard, mirroring <see cref="ChatTwoStyleProvider"/>'s Evaluate: if any pass
    /// throws, the message reverts to its pre-pass state (never left half-styled — the passes
    /// replace Message/Sender with new SeString instances, so the original references stay
    /// valid) and the error logs once instead of Dalamud's own per-subscriber catch printing
    /// an unattributed error for every chat line.
    /// </summary>
    private void OnChatMessage(IHandleableChatMessage message)
    {
        var originalMessage = message.Message;
        var originalSender = message.Sender;
        try
        {
            OnChatMessageCore(message);
        }
        catch (Exception ex)
        {
            message.Message = originalMessage;
            message.Sender = originalSender;
            if (_chatMessageErrorLogged)
                return;

            _chatMessageErrorLogged = true;
            Plugin.Log.Warning(ex, "Chat formatting failed; the message renders unformatted (further failures are not logged until the next settings change)");
        }
    }

    private void OnChatMessageCore(IHandleableChatMessage message)
    {
        SenderIdentity.Resolve(message.Sender, out var name, out var world);
        var sender = new Sender(name, world, IsFromSelf(message));

        // Range outcome first (the distance and mention probe read the raw message), but styling
        // still runs for dimmed messages — the fade then darkens the styled colors in place
        // instead of flattening everything to one grey line.
        var fadeStep = RangeFadeStep(message, sender);

        // Mention detection is independent of the highlighting gate: when the highlighting pass
        // (which plays the sound itself) doesn't run for this message, probe here — bounded to
        // conversational channels — so the sound and the mention history also cover channels
        // that aren't highlighted. The mention outcome feeds the group pass: a message that fired
        // (or could have fired) the mention alert never also plays a group sound (ADR 0005).
        var mentioned = false;
        if (_enabled && _channels.Contains(message.LogKind))
        {
            mentioned = ApplyBodyHighlighting(message, sender, fadeStep);
        }
        else if (_config.Mentions.MentionsEnabled
            && ChatChannels.Conversational.Contains(message.LogKind)
            && HasMention(message))
        {
            mentioned = true;
            TryPlayMentionSound(sender);
        }

        if (mentioned)
            RecordMention(message, sender);

        ApplySenderGroupColor(message, sender, fadeStep, mentioned);

        if (fadeStep is { } step)
            ApplyFade(message, step);
    }

    /// <summary>
    /// Distance outcome of the range filter: null = fully visible, otherwise the fade step to apply
    /// after styling. Beyond the cut-off the message gets the darkest step instead of being
    /// suppressed: PreventOriginal marks a message handled, which drops it before the
    /// ChatMessageUnhandled consumers see it — Chat 2's history and any event-fed chat logger would
    /// silently lose it (Chat 2 can still hide it render-only on its side). An unresolvable sender
    /// (not in the object table, e.g. already gone) stays fully visible.
    /// </summary>
    private int? RangeFadeStep(IHandleableChatMessage message, Sender sender)
    {
        if (!_rangeEnabled || !_rangeChannels.Contains(message.LogKind))
            return null;

        if (SenderDistance.Resolve(sender.Name, sender.World) is not { } distance)
            return null;

        var visibility = RangeFade.CalculateVisibility(
            distance, _config.RangeFilter.RangeFilterFadeOut, _config.RangeFilter.RangeFilterCutOff);
        if (visibility == RangeFade.MaxVisibility)
            return null;

        if (_config.RangeFilter.RangeFilterMentionsIgnoreRange && HasMention(message))
            return null;

        return ResolveFadeStep(visibility);
    }

    /// <summary>
    /// Mention probe for the range filter's bypass and the sound-only mention path: segments the
    /// message body without rewriting anything. Range-bypassed messages pay one extra
    /// segmentation (only messages already inside fade range); the sound-only path segments
    /// exactly once, since it only runs when the highlighting pass doesn't.
    /// </summary>
    private bool HasMention(IHandleableChatMessage message)
        => CollectTextRuns(message.Message.ToRoss().AsSpan()) is { } runTexts
            && _segmenter.Segment(runTexts)?.HasMention == true;

    /// <summary>
    /// Dims sender and body one fade step: colored spans (RP highlighting, group names,
    /// pre-colored link text) keep their hue via UiColorDimmer's darker-row mapping, text
    /// outside any color span falls back to the channel's own configured chat color
    /// (<see cref="ResolveChannelColor"/>), darkened the same way.
    /// </summary>
    private void ApplyFade(IHandleableChatMessage message, int step)
    {
        var uncolored = ResolveChannelColor(message.LogKind);
        message.Message = UiColorDimmer.DimRoss(message.Message.ToRoss().AsSpan(), step, uncolored).ToDalamudString();
        message.Sender = UiColorDimmer.DimRoss(message.Sender.ToRoss().AsSpan(), step, uncolored).ToDalamudString();
    }

    /// <returns>Whether the message body matched a mention — feeds the group-sound precedence
    /// rule in <see cref="TryPlayGroupSound"/> without a second segmentation.</returns>
    private bool ApplyBodyHighlighting(IHandleableChatMessage message, Sender sender, int? fadeStep)
    {
        var body = message.Message.ToRoss();
        if (CollectTextRuns(body.AsSpan()) is not { } runTexts)
            return false;

        // Own messages keep Say/Emote/Ooc styling but skip the mention recolor; detection
        // still runs (overlayMentions: false), so HasMention below keeps feeding the sound,
        // whose own SuppressSoundFromSelf rule decides independently. Echo is exempt even
        // though it counts as self: /echo is the designated way to test mention setups.
        var suppressOwnHighlight = _config.Mentions.SuppressHighlightFromSelf
            && message.LogKind != XivChatType.Echo
            && sender.IsSelf;
        var result = _segmenter.Segment(runTexts, DefaultTypeFor(message.LogKind),
            overlayMentions: _overlayMentions && !suppressOwnHighlight, detectEmote: DetectEmoteFor(message.LogKind));
        if (result == null)
            return false;

        var rewritten = PayloadRewriter.Rewrite(
            body.AsSpan(), runTexts, result.RunSpans, _styles, _mentionWordStyles, fadeStep);
        message.Message = rewritten.ToDalamudString();

        if (result.HasMention)
            TryPlayMentionSound(sender);
        return result.HasMention;
    }

    /// <summary>
    /// Recolors the sender name when it belongs to a matching custom or friend group, and plays
    /// that group's alert sound — the sound is independent of whether the group recolors
    /// anything. Independent of the RP-highlighting master switch and channel filter — group
    /// coloring is its own feature and applies wherever a sender exists.
    /// </summary>
    private void ApplySenderGroupColor(IHandleableChatMessage message, Sender sender, int? fadeStep, bool mentioned)
    {
        if (!ChatChannels.Grouping.Contains(message.LogKind))
            return;

        var (name, world) = SenderCompletion.Complete(
            sender.Name, sender.World, sender.IsSelf, LocalName(),
            Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.HomeWorld.ValueNullable?.Name.ExtractText() : null,
            Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText() : null);

        // A senderless message (system/notification text with no PlayerPayload and no raw name run)
        // stays empty even after completion, which only ever fills in world — never a real sender.
        if (name.Length == 0)
            return;

        var friendGroupIndex = _friendGroups.TryGetFriendGroupIndex(name, world, out var index) ? index : (int?)null;
        var groupId = GroupMatcher.FindGroup(name, world, friendGroupIndex, _groupRules);
        if (groupId == null || !_groupsById.TryGetValue(groupId, out var group))
            return;

        TryPlayGroupSound(group, sender, mentioned);

        if (group.Foreground == 0 && group.Glow == 0)
            return;

        var senderText = message.Sender.ToRoss();
        if (CollectTextRuns(senderText.AsSpan()) is not { } runTexts)
            return;

        var rewritten = PayloadRewriter.RewriteUniform(senderText.AsSpan(), runTexts, (group.Foreground, group.Glow), fadeStep);
        message.Sender = rewritten.ToDalamudString();
    }

    /// <summary>
    /// Extracts the non-empty text-payload runs of a parsed message. Like ChatAlerts, every text
    /// payload counts — including link display text; the rewriters' balanced on/off pairs nest
    /// inside pre-colored regions and pop back correctly. Iterated in the same order and with the
    /// same "non-empty text payload = one run" rule <see cref="PayloadRewriter"/> and
    /// <see cref="UiColorDimmer"/> replay, so spans line up positionally. Null when the message
    /// contains no text at all.
    /// </summary>
    private static List<string>? CollectTextRuns(ReadOnlySeStringSpan source)
    {
        List<string>? runTexts = null;
        foreach (var payload in source)
        {
            if (payload.Type != ReadOnlySePayloadType.Text || payload.Body.Length == 0)
                continue;

            runTexts ??= [];
            runTexts.Add(Encoding.UTF8.GetString(payload.Body));
        }

        return runTexts;
    }

    /// <summary>
    /// Records a matched mention into the in-memory history window. Runs wherever mention
    /// detection already runs (the highlight pass or the sound-only probe) and is independent of
    /// the sound gates: a cooldown-suppressed alert still lands in the history. Own messages are
    /// skipped (Echo exempt — the designated mention test channel). OriginalMessage, like the chat
    /// logger: the history shows what was said, not GobchatEx's recoloring.
    /// </summary>
    private void RecordMention(IHandleableChatMessage message, Sender sender)
    {
        if (message.LogKind != XivChatType.Echo && sender.IsSelf)
            return;

        var text = message.OriginalMessage.ToDalamudString().TextValue;

        // The mention was detected on message.Message (possibly already edited by another
        // plugin), but the history stores the original text — so the detection pass's span
        // offsets can't be reused here. Re-segment the stored string once so the window's
        // highlight and "triggered mentions" column line up with what it shows. When the
        // original text doesn't reproduce the match, both stay empty and the window falls back
        // to plain rendering.
        IReadOnlyList<SegmentSpan> spans = _segmenter.Segment([text]) is { } result
            ? result.RunSpans[0].Where(s => s.Type == SegmentType.Mention).ToArray()
            : [];
        var matches = string.Join(", ", spans
            .Select(s => text.Substring(s.Start, s.Length))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        // Override-only foreground (0 = no override): the window falls back to whatever the
        // default mention color is *at draw time*, so editing that default recolors already
        // recorded entries, while an explicit override survives later settings changes.
        var spanColors = spans
            .Select(s => s.StyleId != 0 ? MentionStyleResolver.Resolve(s.StyleId, _mentionWordStyles, 0, 0).Foreground : 0u)
            .ToArray();

        _mentionHistory.Add(new MentionHistoryEntry(
            message.LocalTimestamp(), message.LogKind, sender.Name, sender.World, text, spans, matches, spanColors));
    }

    /// <summary>
    /// The per-group alert sound, policy per ADR 0005: at most one sound per message — when the
    /// mention alert is armed (mention matched and the mention sound enabled), it is the more
    /// specific signal and the group sound stands down, even if the mention sound then loses to
    /// its own cooldown. Own messages never play a group sound.
    /// </summary>
    private void TryPlayGroupSound(PlayerGroup group, Sender sender, bool mentioned)
    {
        if (!group.SoundEnabled)
            return;

        if (mentioned && _config.Mentions.MentionsEnabled && _config.Mentions.MentionSoundEnabled)
            return;

        if (sender.IsSelf)
        {
            Plugin.Log.Debug("Group sound suppressed: own message");
            return;
        }

        _soundPlayer.TryPlayGroup(group, _config.Groups.GroupSoundCooldownMs);
    }

    private void TryPlayMentionSound(Sender sender)
    {
        if (!_config.Mentions.MentionSoundEnabled)
        {
            Plugin.Log.Debug("Mention matched but the sound alert is disabled");
            return;
        }

        if (_config.Mentions.SuppressSoundFromSelf && sender.IsSelf)
        {
            Plugin.Log.Debug("Mention sound suppressed: own message");
            return;
        }

        _soundPlayer.TryPlay(_config.Mentions);
    }

    /// <summary>
    /// The local character's name from IPlayerState, which (unlike ObjectTable.LocalPlayer) stays
    /// available through zoning and loading screens; empty while not logged in.
    /// </summary>
    private static string LocalName()
        => Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.CharacterName : string.Empty;

    /// <summary>
    /// Heuristic own-message check (see <see cref="SelfSender"/> for the shared string rule).
    /// Gates the self-suppressions of the mention sound/highlight and the group sound.
    /// </summary>
    private static bool IsFromSelf(IHandleableChatMessage message)
        => SelfSender.IsSelf(ChatChannels.IsSelfChannel(message.LogKind), message.Sender.TextValue, LocalName());
}
