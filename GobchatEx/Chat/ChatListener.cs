using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Config;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using GobchatEx.Config;
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
    private Dictionary<SegmentType, (uint Foreground, uint Glow)> _styles = null!;
    private IReadOnlyList<GroupRule> _groupRules = [];
    private Dictionary<string, (uint Foreground, uint Glow)> _groupStyles = new();
    private bool _enabled;
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

    // Tells and Echo carry no real sender to group (Echo is local-only, matching IsFromSelf's own
    // reasoning); error messages are game system text. Mirrors the old app's channel exclusion.
    // Internal so ChatTwoStyleProvider applies the same exclusion to group backgrounds.
    internal static readonly HashSet<XivChatType> GroupingExcludedChannels =
        [XivChatType.TellIncoming, XivChatType.TellOutgoing, XivChatType.Echo, XivChatType.ErrorMessage];

    internal ChatListener(Configuration config, FriendGroupLookup friendGroups)
    {
        _config = config;
        _friendGroups = friendGroups;

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

        _rangeEnabled = _config.RangeFilter.RangeFilterEnabled;
        _rangeChannels = [.. _config.RangeFilter.RangeFilterChannels];
        _chatTwoChannelColors = ChatTwoChannelColors.Read();

        // Mention detection also runs highlight-disabled when the sound alert needs it (the
        // rewriter then renders those spans plain via (0, 0)) or when the range filter's
        // mention bypass needs to know whether a fading message mentions the player.
        var wantMentions = _config.Formatting.MentionStyle.Enabled || _config.Mentions.MentionSoundEnabled
            || (_rangeEnabled && _config.RangeFilter.RangeFilterMentionsIgnoreRange);
        _segmenter = new MessageSegmenter(rules, wantMentions ? BuildMentionRules(_config.Mentions) : NoMentionRules);

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
            return;
        }

        _groupRules = GroupRuleBuilder.Build(_config.Groups, snapshotMembers: false);

        var styles = new Dictionary<string, (uint Foreground, uint Glow)>(_groupRules.Count);
        foreach (var group in _config.Groups.Groups.Concat(_config.Groups.FriendGroups))
            styles[group.Id] = (group.Foreground, group.Glow);

        _groupStyles = styles;
    }

    /// <summary>
    /// Global trigger words plus the currently logged-in character's resolved mention words (if that
    /// character is remembered and active), ported from the app's RecomputePlayerMentions /
    /// ApplyEffectiveMentions. Player-resolved whole words are unioned into the global list,
    /// case-insensitive de-duplicated; partial and fuzzy words are player-only. Static and internal
    /// so ChatTwoStyleProvider builds its mention-bypass segmenter from the same rules; reads
    /// IPlayerState, so callers must be on the framework thread.
    /// </summary>
    internal static MentionRules BuildMentionRules(MentionsConfig config)
    {
        if (!config.MentionsEnabled)
            return NoMentionRules;

        var wholeWords = new List<string>(config.MentionTriggers);
        IReadOnlyList<string> partialWords = [];
        IReadOnlyList<string> fuzzyWords = [];
        var fuzzyLevel = FuzzyMatchLevel.Conservative;

        if (config.PlayerMentionsEnabled && Plugin.PlayerState.IsLoaded)
        {
            var playerName = Plugin.PlayerState.CharacterName;
            var character = config.Characters.FirstOrDefault(c =>
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
        // NoMentionRules with the master switch off).
        if (_enabled && _channels.Contains(message.LogKind))
            ApplyBodyHighlighting(message, fadeStep);
        else if (_config.Mentions.MentionSoundEnabled && _config.Mentions.MentionsEnabled
            && MentionSoundChannels.Contains(message.LogKind)
            && HasMention(message))
            TryPlayMentionSound(message);

        ApplySenderGroupColor(message, fadeStep);

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
        CollectTextRuns(message.Message.Payloads, out _, out var runTexts);
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
        message.Message = new SeString(UiColorDimmer.DimPayloads(message.Message.Payloads, step, uncolored));
        message.Sender = new SeString(UiColorDimmer.DimPayloads(message.Sender.Payloads, step, uncolored));
    }

    private void ApplyBodyHighlighting(IHandleableChatMessage message, int? fadeStep)
    {
        var payloads = message.Message.Payloads;

        CollectTextRuns(payloads, out var runIndices, out var runTexts);
        if (runTexts == null)
            return;

        var result = _segmenter.Segment(runTexts, DefaultTypeFor(message.LogKind));
        if (result == null)
            return;

        var rewritten = PayloadRewriter.Rewrite(payloads, runIndices!, runTexts, result.RunSpans, _styles, fadeStep);
        message.Message = new SeString(rewritten);

        if (result.HasMention)
            TryPlayMentionSound(message);
    }

    /// <summary>
    /// Recolors the sender name when it belongs to a matching custom or friend group. Independent of
    /// <see cref="_enabled"/>/<see cref="_channels"/> (the RP-highlighting master switch and channel
    /// filter) — group coloring is its own feature and applies wherever a sender exists.
    /// </summary>
    private void ApplySenderGroupColor(IHandleableChatMessage message, int? fadeStep)
    {
        if (GroupingExcludedChannels.Contains(message.LogKind))
            return;

        SenderIdentity.Resolve(message.Sender, out var name, out var world);
        if (world == null)
            ResolveWorldlessSender(message, ref name, ref world);

        var friendGroupIndex = _friendGroups.TryGetFriendGroupIndex(name, world, out var index) ? index : (int?)null;

        var groupId = GroupMatcher.FindGroup(name, world, friendGroupIndex, _groupRules);
        if (groupId == null
            || !_groupStyles.TryGetValue(groupId, out var style)
            || (style.Foreground == 0 && style.Glow == 0))
            return;

        var payloads = message.Sender.Payloads;
        CollectTextRuns(payloads, out var runIndices, out var runTexts);
        if (runTexts == null)
            return;

        var rewritten = PayloadRewriter.RewriteUniform(payloads, runIndices!, runTexts, style, fadeStep);
        message.Sender = new SeString(rewritten);
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
    /// Extracts the non-empty TextPayload runs as parallel index/text lists for PayloadRewriter.
    /// Like ChatAlerts, every TextPayload counts — including link display text; the rewriters'
    /// balanced on/off pairs nest inside pre-colored regions and pop back correctly. Both outputs
    /// are null when the payload list contains no text at all.
    /// </summary>
    private static void CollectTextRuns(
        IReadOnlyList<Payload> payloads, out List<int>? runIndices, out List<string>? runTexts)
    {
        runIndices = null;
        runTexts = null;
        for (var i = 0; i < payloads.Count; ++i)
        {
            if (payloads[i] is not TextPayload { Text.Length: > 0 } textPayload)
                continue;

            runIndices ??= [];
            runTexts ??= [];
            runIndices.Add(i);
            runTexts.Add(textPayload.Text);
        }
    }

    private void TryPlayMentionSound(IHandleableChatMessage message)
    {
        if (!_config.Mentions.MentionSoundEnabled)
            return;
        if (_config.Mentions.SuppressSoundFromSelf && IsFromSelf(message))
            return;

        _soundPlayer.TryPlay(_config.Mentions.MentionSoundEffect, _config.Mentions.MentionSoundCooldownMs);
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
    /// Only the sound is suppressed for own messages, never the highlighting.
    /// </summary>
    private static bool IsFromSelf(IHandleableChatMessage message)
        => SelfSender.IsSelf(
            IsSelfChannel(message.LogKind),
            message.Sender.TextValue,
            Plugin.ObjectTable.LocalPlayer?.Name.TextValue);
}
