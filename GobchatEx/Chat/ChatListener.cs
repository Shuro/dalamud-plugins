using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Config;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using GobchatEx.Config;
using GobchatEx.Core;
using Lumina.Text.ReadOnly;

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
    private readonly SoundPlayer _soundPlayer;
    private readonly MentionHistory _mentionHistory;

    private MessageSegmenter _segmenter = null!;
    private HashSet<XivChatType> _channels = null!;
    private Dictionary<SegmentType, (uint Foreground, uint Glow)> _styles = null!;

    // Per-word mention color-override table (StyleId - 1 indexes it), resolved via
    // MentionStyleResolver. _mentionWordStyles is always populated whenever mentions are detected
    // at all (feeds MentionHistory regardless of whether the Mention style itself is on);
    // _mentionRenderStyles is the same table gated by MentionStyle.Enabled, so a per-word override
    // never renders in chat while the master Mention style is off (mirrors _styles[Mention]
    // collapsing to (0,0) in that case).
    private IReadOnlyList<(uint Foreground, uint Glow)> _mentionWordStyles = [];
    private IReadOnlyList<(uint Foreground, uint Glow)> _mentionRenderStyles = [];
    private IReadOnlyList<GroupRule> _groupRules = [];
    private Dictionary<string, (uint Foreground, uint Glow)> _groupStyles = new();
    private Dictionary<string, PlayerGroup> _groupsById = new();
    private bool _enabled;
    private bool _detectEmoteInSay;
    private bool _detectEmoteInParty;
    private bool _rangeEnabled;
    private HashSet<XivChatType> _rangeChannels = [];
    private Dictionary<XivChatType, uint> _chatTwoChannelColors = [];
    private bool _chatMessageErrorLogged;

    // Six fade-step buckets: index 0 is a never-applied "full visibility" reference (production
    // always bypasses ApplyFade for visibility==100 via RangeFadeStep's null return; kept purely
    // so ResolveFadeStep and the Debug page have a coherent 0-5 range), 1-4 are the four
    // partial-fade buckets, 5 is the darkest/beyond-cut-off step. No longer a per-message text
    // color (see ResolveChannelColor) — these UIColor rows now only back the Debug page's
    // reference-shade swatches (stepPreviewColors) and ResolveFadeStep's bucket count. Rows 3-5
    // are the original trio (shipped since Milestone 3, tuned in-game); 1, 2, 6 remain UNVERIFIED
    // guesses extending the ramp. Internal so the Debug page's range pane can preview these rows.
    internal static readonly ushort[] FadeStepColors = [1, 2, 3, 4, 5, 6];

    // The player's own Log Text Color per range-filterable channel (Character Configuration →
    // Log Text Color), read live via ResolveChannelColor so a mid-session change is picked up
    // immediately — these are the only 5 channels the Range tab offers (RangeTab.cs:32-39).
    // Internal so the Debug page's range pane can preview the same resolution.
    internal static readonly Dictionary<XivChatType, UiConfigOption> RangeChannelColorOptions = new()
    {
        [XivChatType.Say] = UiConfigOption.ColorSay,
        [XivChatType.CustomEmote] = UiConfigOption.ColorEmoteUser,
        [XivChatType.StandardEmote] = UiConfigOption.ColorEmote,
        [XivChatType.Yell] = UiConfigOption.ColorYell,
        [XivChatType.Shout] = UiConfigOption.ColorShout,
    };

    // Used only when a channel has no RangeChannelColorOptions entry or the game config read
    // fails/is unset — shouldn't happen in practice since RangeFadeStep already gates on
    // _rangeChannels, which the Range tab only ever populates from that same channel set.
    internal const uint FallbackFadeColor = 0x808080FF;

    /// <summary>
    /// The channel's own configured chat color (config-storage 0xRRGGBBAA, not yet dimmed) —
    /// what a plain, unformatted message on that channel fades from, so Yell stays yellowish and
    /// Shout stays orange-red instead of every channel collapsing into one shared grey. Prefers
    /// Chat 2's own customized color for that channel when one is on file (<see
    /// cref="_chatTwoChannelColors"/>, refreshed in <see cref="SettingsChanged"/>) — an explicit
    /// Color macro permanently overrides Chat 2's own per-channel rendering, so baking in
    /// vanilla's color instead would mismatch every non-faded message on that channel for a Chat 2
    /// user who customized it. Falls back to the vanilla <see cref="RangeChannelColorOptions"/>
    /// read, then <see cref="FallbackFadeColor"/> when neither is available.
    /// </summary>
    internal uint ResolveChannelColor(XivChatType channel) => ResolveChannelColorWithSource(channel).Color;

    /// <summary>
    /// Same resolution as <see cref="ResolveChannelColor"/>, also reporting which tier won —
    /// used only by the Debug page's range pane to label each channel's color source.
    /// <paramref name="liveChatTwoRead"/> re-reads Chat 2's config file on the spot instead of
    /// trusting <see cref="_chatTwoChannelColors"/>'s cache (refreshed only on <see
    /// cref="SettingsChanged"/>) — production never sets this, since a per-message file read would
    /// defeat the point of caching, but a manually-clicked Debug button is infrequent enough that a
    /// live read costs nothing and avoids the cache showing a Chat 2 edit made moments ago as still
    /// "vanilla".
    /// </summary>
    internal (uint Color, string Source) ResolveChannelColorWithSource(XivChatType channel, bool liveChatTwoRead = false)
    {
        var chatTwoColors = liveChatTwoRead ? ChatTwoChannelColors.Read() : _chatTwoChannelColors;

        if (chatTwoColors.TryGetValue(channel, out var chatTwoColor) && chatTwoColor != 0)
            return (chatTwoColor, "Chat 2");

        if (RangeChannelColorOptions.TryGetValue(channel, out var option)
            && Plugin.GameConfig.TryGet(option, out uint raw)
            && RgbaColor.FromGameConfigColor(raw) is { } color)
            return (color, "vanilla");

        return (FallbackFadeColor, "fallback");
    }

    /// <summary>
    /// Resolves the 0-5 fade step for 0 &lt;= visibility &lt; 100. Callers special-case
    /// visibility == <see cref="RangeFade.MaxVisibility"/> themselves — 100 never reaches here
    /// (<see cref="RangeFadeStep"/> bypasses fading entirely; the Debug page's range pane prints
    /// "fully visible" instead). visibility == 0 maps straight to the darkest entry; 1-4 come
    /// from splitting the open interval into four buckets via <see cref="RangeFade.FadeStep"/>,
    /// offset by one so step 0 stays reserved as the Debug page's unreachable-in-production
    /// reference.
    /// </summary>
    internal static int ResolveFadeStep(int visibility) => visibility == 0
        ? FadeStepColors.Length - 1
        : 1 + RangeFade.FadeStep(visibility, FadeStepColors.Length - 2);

    private static readonly MentionRules NoMentionRules = new([], [], [], FuzzyMatchLevel.Conservative);

    // The channel universe the sound-only mention probe listens on: every conversational channel
    // the Formatting tab offers (its Main/Linkshell/Crossworld grids — keep in sync), independent
    // of which ones the user selected for highlighting. CheckMessageHandled also fires for combat,
    // loot, and system LogKinds, which must stay out: battle log lines routinely contain player
    // names (false mention dings) and would pay the segmentation scan on every non-chat line.
    private static readonly HashSet<XivChatType> MentionSoundChannels =
    [
        XivChatType.Say, XivChatType.CustomEmote, XivChatType.StandardEmote,
        XivChatType.Yell, XivChatType.Shout,
        XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance,
        XivChatType.FreeCompany, XivChatType.TellIncoming, XivChatType.TellOutgoing,
        XivChatType.NoviceNetwork, XivChatType.Echo,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3,
        XivChatType.CrossLinkShell4, XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    // Group coloring/sound only ever applies to a real conversing player, so this reuses
    // MentionSoundChannels' conversational-channel universe rather than denylisting — CheckMessageHandled
    // fires the full XivChatType enum, including ~20 senderless system/notification types (Teleport,
    // zone-leave, Party Finder notices, gil-spent text, etc.) and the combat/loot/craft log, none of
    // which should ever paint a sender or ding a group sound. An allow-list fails closed against those
    // (and any future Dalamud chat type) instead of needing a denylist entry for every one of them.
    // Tells and Echo are subtracted back out: they carry no real sender to group (Echo is local-only,
    // matching IsFromSelf's own reasoning) — mirrors the old app's channel exclusion. Internal so
    // ChatTwoStyleProvider applies the same scope to group backgrounds.
    internal static readonly HashSet<XivChatType> GroupingChannels =
        [.. MentionSoundChannels.Except([XivChatType.TellIncoming, XivChatType.TellOutgoing, XivChatType.Echo])];

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

    /// <summary>Call after any configuration change (and Save()).</summary>
    public void SettingsChanged()
    {
        _chatMessageErrorLogged = false;
        _enabled = _config.Formatting.RpHighlightEnabled;
        _channels = [.. _config.Formatting.HighlightChannels];
        _styles = new Dictionary<SegmentType, (uint, uint)>
        {
            [SegmentType.Say] = StyleTuple(_config.Formatting.SayStyle),
            [SegmentType.Emote] = StyleTuple(_config.Formatting.EmoteStyle),
            [SegmentType.Ooc] = StyleTuple(_config.Formatting.OocStyle),
            [SegmentType.Mention] = StyleTuple(_config.Formatting.MentionStyle),
        };

        var rules = DefaultRules.All.Where(rule => StyleFor(rule.Type).Enabled).ToList();

        // Gated on the Emote style, mirroring DefaultTypeFor's "a disabled style never produces
        // that SegmentType" rule — otherwise leftovers would be typed Emote and render plain via
        // the (0, 0) tuple instead of falling back to the Say channel default. The Say side needs
        // no gate: with SayStyle disabled no Say token rule is built (above), so no quoted span
        // can ever exist and detection is naturally inert.
        _detectEmoteInSay = _config.Formatting.DetectEmoteInSay && _config.Formatting.EmoteStyle.Enabled;
        _detectEmoteInParty = _config.Formatting.DetectEmoteInParty && _config.Formatting.EmoteStyle.Enabled;

        _rangeEnabled = _config.RangeFilter.RangeFilterEnabled;
        _rangeChannels = [.. _config.RangeFilter.RangeFilterChannels];
        _chatTwoChannelColors = ChatTwoChannelColors.Read();

        // Mention detection also runs highlight-disabled when the sound alert needs it (the
        // rewriter then renders those spans plain via (0, 0)) or when the range filter's
        // mention bypass needs to know whether a fading message mentions the player.
        var wantMentions = _config.Formatting.MentionStyle.Enabled || _config.Mentions.MentionSoundEnabled
            || (_rangeEnabled && _config.RangeFilter.RangeFilterMentionsIgnoreRange);
        var mentionRules = wantMentions ? BuildMentionRules(_config.Mentions) : NoMentionRules;
        _segmenter = new MessageSegmenter(rules, mentionRules);
        _mentionWordStyles = mentionRules.Styles ?? [];
        _mentionRenderStyles = _config.Formatting.MentionStyle.Enabled ? _mentionWordStyles : [];

        BuildGroupRules();
    }

    // Rule ordering (the precedence invariant) lives in GroupRuleBuilder, shared with the Chat 2
    // provider; only the foreground/glow style lookup is native-pass-specific.
    private void BuildGroupRules()
    {
        if (!_config.Groups.GroupsEnabled)
        {
            _groupRules = [];
            _groupStyles = new();
            _groupsById = new();
            return;
        }

        _groupRules = GroupRuleBuilder.Build(_config.Groups, snapshotMembers: false);

        var styles = new Dictionary<string, (uint Foreground, uint Glow)>(_groupRules.Count);
        var byId = new Dictionary<string, PlayerGroup>(_groupRules.Count);
        foreach (var group in _config.Groups.Groups.Concat(_config.Groups.FriendGroups))
        {
            styles[group.Id] = (group.Foreground, group.Glow);
            byId[group.Id] = group;
        }

        _groupStyles = styles;
        _groupsById = byId;
    }

    /// <summary>
    /// Global trigger words plus the currently logged-in character's resolved mention words (if that
    /// character is remembered and active), ported from the app's RecomputePlayerMentions /
    /// ApplyEffectiveMentions. Maps config onto <see cref="MentionRuleBuilder"/>'s plain inputs,
    /// which owns the actual union/dedupe/style-id-allocation logic (kept Config-free so it's
    /// directly testable, per ADR 0002). Static and internal so ChatTwoStyleProvider builds its
    /// mention-bypass segmenter from the same rules; reads IPlayerState, so callers must be on the
    /// framework thread.
    /// </summary>
    internal static MentionRules BuildMentionRules(MentionsConfig config)
    {
        if (!config.MentionsEnabled)
            return NoMentionRules;

        var globalTriggers = config.MentionTriggers
            .Select(t => new StyledTrigger(t.Word, t.Foreground, t.Glow))
            .ToList();

        CharacterMentionInput? character = null;
        if (config.PlayerMentionsEnabled && Plugin.PlayerState.IsLoaded)
        {
            var playerName = Plugin.PlayerState.CharacterName;
            var match = config.Characters.FirstOrDefault(c =>
                c.Active && string.Equals(c.Name, playerName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                character = new CharacterMentionInput(
                    match.Name,
                    match.MatchFullName,
                    match.MatchFirstName,
                    match.MatchLastName,
                    match.MatchFirstNamePartial,
                    match.MatchLastNamePartial,
                    match.MatchMiqote,
                    match.MatchFuzzy,
                    match.FuzzyLevel,
                    match.NameForeground,
                    match.NameGlow,
                    match.CustomWords.Select(w => new StyledTrigger(w.Word, w.Foreground, w.Glow)).ToList());
            }
        }

        return MentionRuleBuilder.Build(globalTriggers, character);
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
    /// A /say (or its /s alias) or /emote (or /em) line is implicitly said/emoted even without the
    /// usual quote or asterisk delimiters — Dalamud already resolves both aliases to the same
    /// <see cref="XivChatType"/> as their long form, so no literal command-text matching is needed.
    /// Only applies when the corresponding style is enabled, matching <see cref="SettingsChanged"/>'s
    /// existing rule that a disabled style never produces that <see cref="SegmentType"/>.
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
    /// cross-world party are paired like everywhere else (e.g.
    /// <see cref="FormattingConfig.DefaultHighlightChannels"/>).
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
            Plugin.Log.Warning(ex, "Chat formatting failed; messages render unformatted until the next settings change");
        }
    }

    private void OnChatMessageCore(IHandleableChatMessage message)
    {
        // Range outcome first (the distance and mention probe read the raw message), but styling
        // still runs for dimmed messages — the fade then darkens the styled colors in place
        // instead of flattening everything to one grey line.
        var fadeStep = RangeFadeStep(message);

        // The mention sound is independent of the highlighting gate: SettingsChanged builds
        // mention rules whenever the sound alert needs them, so when the highlighting pass
        // (which plays the sound itself) doesn't run for this message, probe here — bounded
        // to conversational channels (see MentionSoundChannels). The MentionsEnabled guard
        // skips a per-message segmentation that could never match (BuildMentionRules returns
        // NoMentionRules with the master switch off). The mention outcome feeds the group
        // pass: a message that fired (or could have fired) the mention alert never also plays
        // a group sound (ADR 0005).
        var mentioned = false;
        if (_enabled && _channels.Contains(message.LogKind))
        {
            mentioned = ApplyBodyHighlighting(message, fadeStep);
        }
        else if (_config.Mentions.MentionSoundEnabled && _config.Mentions.MentionsEnabled
            && MentionSoundChannels.Contains(message.LogKind)
            && HasMention(message))
        {
            mentioned = true;
            TryPlayMentionSound(message);
        }

        if (mentioned)
            RecordMention(message);

        ApplySenderGroupColor(message, fadeStep, mentioned);

        if (fadeStep is { } step)
            ApplyFade(message, step, message.LogKind);
    }

    /// <summary>
    /// Distance outcome of the range filter (Milestone 3): null = fully visible, otherwise the
    /// 0-based fade step to apply after styling. Beyond the cut-off the message gets the darkest
    /// step instead of being suppressed: PreventOriginal marks a message handled, which drops it
    /// before the ChatMessageUnhandled consumers see it — Chat 2's history and any event-fed chat
    /// logger would silently lose it (Chat 2 can still hide it render-only on its side). An
    /// unresolvable sender (not in the object table, e.g. already gone) stays fully visible —
    /// the app's deliberate rule, ported as-is.
    /// </summary>
    private int? RangeFadeStep(IHandleableChatMessage message)
    {
        if (!_rangeEnabled || !_rangeChannels.Contains(message.LogKind))
            return null;

        SenderIdentity.Resolve(message.Sender, out var name, out var world);
        if (SenderDistance.Resolve(name, world) is not { } distance)
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
    /// message body without rewriting anything. Costs one extra segmentation for range-bypassed
    /// messages (which then get segmented again by <see cref="ApplyBodyHighlighting"/>), only
    /// paid by messages already inside fade range; the sound-only path segments exactly once,
    /// since it only runs when the highlighting pass doesn't.
    /// </summary>
    private bool HasMention(IHandleableChatMessage message)
    {
        var body = new ReadOnlySeString(message.Message.Encode());
        CollectTextRuns(body.AsSpan(), out var runTexts);
        if (runTexts == null)
            return false;

        return _segmenter.Segment(runTexts)?.HasMention == true;
    }

    /// <summary>
    /// Dims sender and body one fade step: colored spans (RP highlighting, group names,
    /// pre-colored link text) keep their hue via UiColorDimmer's darker-row mapping, text
    /// outside any color span falls back to <paramref name="channel"/>'s own configured chat
    /// color (<see cref="ResolveChannelColor"/>), darkened the same way — so it keeps its hue
    /// instead of every channel collapsing into one shared grey.
    /// </summary>
    private void ApplyFade(IHandleableChatMessage message, int step, XivChatType channel)
    {
        var uncolored = ResolveChannelColor(channel);
        var body = new ReadOnlySeString(message.Message.Encode());
        message.Message = UiColorDimmer.DimRoss(body.AsSpan(), step, uncolored).ToDalamudString();
        var sender = new ReadOnlySeString(message.Sender.Encode());
        message.Sender = UiColorDimmer.DimRoss(sender.AsSpan(), step, uncolored).ToDalamudString();
    }

    /// <returns>Whether the message body matched a mention — feeds the group-sound precedence
    /// rule in <see cref="TryPlayGroupSound"/> without a second segmentation.</returns>
    private bool ApplyBodyHighlighting(IHandleableChatMessage message, int? fadeStep)
    {
        var body = new ReadOnlySeString(message.Message.Encode());

        CollectTextRuns(body.AsSpan(), out var runTexts);
        if (runTexts == null)
            return false;

        // Own messages keep Say/Emote/Ooc styling but skip the mention recolor; detection
        // still runs (overlayMentions: false), so HasMention below keeps feeding the sound,
        // whose own SuppressSoundFromSelf rule decides independently. Echo is exempt even
        // though IsFromSelf counts it as self: /echo is the designated way to test mention
        // setups, so it always keeps the highlight.
        var suppressOwnHighlight = _config.Mentions.SuppressHighlightFromSelf
            && message.LogKind != XivChatType.Echo
            && IsFromSelf(message);
        var result = _segmenter.Segment(runTexts, DefaultTypeFor(message.LogKind),
            overlayMentions: !suppressOwnHighlight, detectEmote: DetectEmoteFor(message.LogKind));
        if (result == null)
            return false;

        var rewritten = PayloadRewriter.Rewrite(
            body.AsSpan(), runTexts, result.RunSpans, _styles, _mentionRenderStyles, fadeStep);
        message.Message = rewritten.ToDalamudString();

        if (result.HasMention)
            TryPlayMentionSound(message);
        return result.HasMention;
    }

    /// <summary>
    /// Recolors the sender name when it belongs to a matching custom or friend group, and plays
    /// that group's alert sound (Milestone 6) — the sound is independent of whether the group
    /// recolors anything. Independent of <see cref="_enabled"/>/<see cref="_channels"/> (the
    /// RP-highlighting master switch and channel filter) — group coloring is its own feature and
    /// applies wherever a sender exists.
    /// </summary>
    private void ApplySenderGroupColor(IHandleableChatMessage message, int? fadeStep, bool mentioned)
    {
        if (!GroupingChannels.Contains(message.LogKind))
            return;

        SenderIdentity.Resolve(message.Sender, out var name, out var world);
        if (world == null)
            ResolveWorldlessSender(message, ref name, ref world);

        // A senderless message (system/notification text with no PlayerPayload and no raw name run)
        // stays empty here even after the fallback above, which only ever fills in world — never
        // treat that as a real sender to group. Mirrors ChatTwoStyleProvider.EvaluateCore's own
        // name.Length > 0 guard.
        if (name.Length == 0)
            return;

        var friendGroupIndex = _friendGroups.TryGetFriendGroupIndex(name, world, out var index) ? index : (int?)null;

        var groupId = GroupMatcher.FindGroup(name, world, friendGroupIndex, _groupRules);
        if (groupId == null)
            return;

        TryPlayGroupSound(message, groupId, mentioned);

        if (!_groupStyles.TryGetValue(groupId, out var style)
            || (style.Foreground == 0 && style.Glow == 0))
            return;

        var sender = new ReadOnlySeString(message.Sender.Encode());
        CollectTextRuns(sender.AsSpan(), out var runTexts);
        if (runTexts == null)
            return;

        var rewritten = PayloadRewriter.RewriteUniform(sender.AsSpan(), runTexts, style, fadeStep);
        message.Sender = rewritten.ToDalamudString();
    }

    /// <summary>
    /// Completes a sender that resolved without a world. The local player's own posts are the main
    /// case: they carry no PlayerPayload (you aren't an interactable sender to yourself), so
    /// SenderIdentity falls back to raw text — no world suffix, and possibly a party-number prefix
    /// on the name. A world-qualified group member (the context menu and /gobchat group always store
    /// one) would then never match; substitute the local player's clean name and home world instead.
    /// Any other world-less sender is standing on the current world (visitors always render with a
    /// cross-world suffix), the same fallback FriendGroupLookup.TryGetFriendGroupIndex applies.
    /// </summary>
    private static void ResolveWorldlessSender(IHandleableChatMessage message, ref string name, ref string? world)
    {
        if (IsFromSelf(message))
        {
            var local = Plugin.ObjectTable.LocalPlayer;
            if (local == null)
                return;

            name = local.Name.TextValue;
            world = local.HomeWorld.ValueNullable?.Name.ExtractText();
            return;
        }

        world = Plugin.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText();
    }

    /// <summary>
    /// Extracts the non-empty text-payload runs of a parsed message. Like ChatAlerts, every text
    /// payload counts — including link display text; the rewriters' balanced on/off pairs nest
    /// inside pre-colored regions and pop back correctly. Iterated in the same order and with the
    /// same "non-empty text payload = one run" rule <see cref="PayloadRewriter"/> and
    /// <see cref="UiColorDimmer"/> replay, so spans line up positionally. Null when the message
    /// contains no text at all.
    /// </summary>
    private static void CollectTextRuns(ReadOnlySeStringSpan source, out List<string>? runTexts)
    {
        runTexts = null;
        foreach (var payload in source)
        {
            if (payload.Type != ReadOnlySePayloadType.Text || payload.Body.Length == 0)
                continue;

            runTexts ??= [];
            runTexts.Add(Encoding.UTF8.GetString(payload.Body));
        }
    }

    /// <summary>
    /// Records a matched mention into the in-memory history window (Milestone 7). Runs wherever
    /// mention detection already runs (the highlight pass or the sound-only probe) — it adds no
    /// detection path of its own — and is independent of the sound gates: a cooldown-suppressed
    /// alert still lands in the history. Own messages are skipped, mirroring the highlight
    /// suppression's rule (Echo exempt — the designated mention test channel). OriginalMessage,
    /// like the chat logger: the history shows what was said, not GobchatEx's recoloring.
    /// </summary>
    private void RecordMention(IHandleableChatMessage message)
    {
        if (message.LogKind != XivChatType.Echo && IsFromSelf(message))
            return;

        var timestamp = message.Timestamp > 0
            ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp).ToLocalTime()
            : DateTimeOffset.Now;
        SenderIdentity.Resolve(message.Sender, out var name, out var world);
        var text = message.OriginalMessage.ToDalamudString().TextValue;

        // The mention was detected on message.Message (possibly already edited by another
        // plugin), but the history stores the original text — so the detection pass's span
        // offsets can't be reused here. Re-segment the stored string once so the window's
        // highlight and "triggered mentions" column line up with what it shows; one short
        // string per recorded mention is trivial. When the original text doesn't reproduce
        // the match (the mention only existed in the edited message), both stay empty and
        // the window falls back to plain rendering.
        IReadOnlyList<SegmentSpan> spans = _segmenter.Segment([text]) is { } result
            ? result.RunSpans[0].Where(s => s.Type == SegmentType.Mention).ToArray()
            : [];
        var matches = string.Join(", ", spans
            .Select(s => text.Substring(s.Start, s.Length))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        // Override-only foreground (0 = no override): the window falls back to whatever the
        // default mention color is *at draw time*, so editing that default keeps recoloring
        // already-recorded entries exactly like before per-word overrides existed, while an
        // explicit override survives later settings changes untouched.
        var spanColors = spans
            .Select(s => s.StyleId != 0 ? MentionStyleResolver.Resolve(s.StyleId, _mentionWordStyles, 0, 0).Foreground : 0u)
            .ToArray();

        _mentionHistory.Add(new MentionHistoryEntry(
            timestamp, message.LogKind, name, world, text, spans, matches, spanColors));
    }

    /// <summary>
    /// The per-group alert sound (Milestone 6), policy per ADR 0005: at most one sound per
    /// message — when the mention alert is armed (mention matched and the mention sound
    /// enabled), it is the more specific signal and the group sound stands down, even if the
    /// mention sound then loses to its own cooldown. Own messages never play a group sound;
    /// unlike the mention sound there is no opt-out — hearing your own group's ding on every
    /// line you send is never useful.
    /// </summary>
    private void TryPlayGroupSound(IHandleableChatMessage message, string groupId, bool mentioned)
    {
        if (!_groupsById.TryGetValue(groupId, out var group) || !group.SoundEnabled)
            return;

        if (mentioned && _config.Mentions.MentionsEnabled && _config.Mentions.MentionSoundEnabled)
            return;

        if (IsFromSelf(message))
        {
            Plugin.Log.Debug("Group sound suppressed: own message");
            return;
        }

        _soundPlayer.TryPlayGroup(group, _config.Groups.GroupSoundCooldownMs);
    }

    private void TryPlayMentionSound(IHandleableChatMessage message)
    {
        if (!_config.Mentions.MentionSoundEnabled)
        {
            Plugin.Log.Debug("Mention matched but the sound alert is disabled");
            return;
        }

        if (_config.Mentions.SuppressSoundFromSelf && IsFromSelf(message))
        {
            Plugin.Log.Debug("Mention sound suppressed: own message");
            return;
        }

        _soundPlayer.TryPlay(_config.Mentions);
    }

    /// <summary>
    /// TellOutgoing and Echo are unconditionally self: a Tell you sent, or a local-only /echo
    /// print that carries no sender at all — Dalamud's own self-test identifies Echo messages
    /// purely by Message text for the same reason. Internal so the Chat 2 provider applies the
    /// same channel rule before calling the shared <see cref="SelfSender"/> heuristic.
    /// </summary>
    internal static bool IsSelfChannel(XivChatType type)
        => type is XivChatType.TellOutgoing or XivChatType.Echo;

    /// <summary>
    /// Heuristic own-message check (see <see cref="SelfSender"/> for the shared string rule).
    /// Gates both self-suppressions: the mention sound (<see cref="MentionsConfig.SuppressSoundFromSelf"/>)
    /// and the mention highlight (<see cref="MentionsConfig.SuppressHighlightFromSelf"/>).
    /// </summary>
    private static bool IsFromSelf(IHandleableChatMessage message)
        => SelfSender.IsSelf(
            IsSelfChannel(message.LogKind),
            message.Sender.TextValue,
            Plugin.ObjectTable.LocalPlayer?.Name.TextValue);
}
