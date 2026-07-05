using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Production consumer of Chat 2's message styling IPC (Milestone 3.5): renders per-group message
/// backgrounds and true per-message range fade/hide inside Chat 2, on top of the native log's
/// "lite" behavior (sender recoloring, darkened-step dimming), which keeps working without Chat 2.
/// Registers the provider gate when <c>ChatTwo.StyleVersion</c> reports a supported version,
/// re-registers on <c>ChatTwo.Available</c>, and exposes <see cref="IsConnected"/> so the settings
/// UI can disable the Chat 2-only options with a hint.
/// <para>
/// Threading: Chat 2 calls <see cref="Evaluate"/> once per message on its processing thread. All
/// decision inputs live in one immutable <see cref="Snapshot"/> built on the framework thread
/// (on construction, login/logout and <see cref="SettingsChanged"/>) and swapped atomically —
/// range distances come from an object-table snapshot refreshed on framework ticks
/// (<see cref="OnFrameworkUpdate"/>) and read lock-free. The provider thread never touches
/// Dalamud services or waits on the framework thread: the contract wants providers to return
/// quickly, and blocking there could deadlock Chat 2's unload, which holds the framework thread
/// until its message thread — potentially sitting right here — has exited.
/// </para>
/// </summary>
internal sealed class ChatTwoStyleProvider : IDisposable
{
    internal const string ProviderGateName = "GobchatEx.MessageStyle";
    private const int SupportedStyleVersion = 1;

    // Per-tab suppress-flags understood by ChatTwo.SetTabStylePolicies (see the IPC contract).
    internal const int SuppressBackground = 1;
    internal const int SuppressFade = 2;
    internal const int SuppressHide = 4;

    /// <summary>
    /// Alpha used beyond the cut-off when hiding in Chat 2 is disabled but fading is on: the
    /// native filter would suppress the message entirely, so render it at the strongest fade
    /// that is still readable rather than alpha 0 (which the IPC treats as hidden).
    /// </summary>
    private const float HiddenFallbackAlpha = 0.1f;

    /// <summary>
    /// Refresh cadence of the distance snapshot. Chat fading doesn't need frame-exact positions
    /// (the app measured "at the moment the message arrives" too); a quarter second keeps the
    /// per-tick object-table walk negligible.
    /// </summary>
    private const int DistanceRefreshIntervalMs = 250;

    private sealed record Snapshot(
        bool RangeEnabled,
        float FadeOut,
        float CutOff,
        bool ChatTwoFade,
        bool ChatTwoHide,
        HashSet<ushort> RangeChannels,
        IReadOnlyList<GroupRule> GroupRules,
        Dictionary<string, uint> GroupBackgrounds,
        MessageSegmenter? MentionSegmenter,
        string LocalName,
        string? LocalHomeWorld,
        string? LocalCurrentWorld);

    private readonly Configuration _config;
    private readonly FriendGroupLookup _friendGroups;

    private readonly ICallGateSubscriber<int> _styleVersion;
    private readonly ICallGateSubscriber<string, object?> _setProvider;
    private readonly ICallGateSubscriber<object?> _available;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>> _getTabs;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>, object?> _tabsChanged;
    private readonly ICallGateSubscriber<Dictionary<Guid, int>, object?> _setTabPolicies;
    private readonly ICallGateProvider<string, string, ulong, ushort, string, string, (uint, float)> _provider;

    private volatile Snapshot? _snapshot;
    private volatile List<PlayerDistance>? _playerDistances;
    private long _nextDistanceRefresh;
    private bool _suspended;
    private bool _disposed;
    private bool _evaluateErrorLogged;

    /// <summary>Last StyleVersion query succeeded with a supported version. Read by the settings UI.</summary>
    internal bool IsConnected { get; private set; }

    /// <summary>Chat 2's tabs (persistent id → name), from GetTabs/TabsChanged. Read by the settings UI.</summary>
    internal Dictionary<Guid, string> KnownTabs { get; private set; } = [];

    public ChatTwoStyleProvider(Configuration config, FriendGroupLookup friendGroups)
    {
        _config = config;
        _friendGroups = friendGroups;

        _styleVersion = Plugin.PluginInterface.GetIpcSubscriber<int>("ChatTwo.StyleVersion");
        _setProvider = Plugin.PluginInterface.GetIpcSubscriber<string, object?>("ChatTwo.SetMessageStyleProvider");
        _available = Plugin.PluginInterface.GetIpcSubscriber<object?>("ChatTwo.Available");
        _getTabs = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("ChatTwo.GetTabs");
        _tabsChanged = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>, object?>("ChatTwo.TabsChanged");
        _setTabPolicies = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, int>, object?>("ChatTwo.SetTabStylePolicies");

        _provider = Plugin.PluginInterface
            .GetIpcProvider<string, string, ulong, ushort, string, string, (uint, float)>(ProviderGateName);
        _provider.RegisterFunc(Evaluate);

        _available.Subscribe(OnAvailable);
        _tabsChanged.Subscribe(OnTabsChanged);
        Plugin.ClientState.Login += OnLoginStateChanged;
        Plugin.ClientState.Logout += OnLogout;
        Plugin.Framework.Update += OnFrameworkUpdate;

        // Plugin construction isn't guaranteed framework-thread (no LoadSync); the snapshot reads
        // IPlayerState, so dispatch — Chat 2 can't call the gate before it exists anyway. The
        // disposed guard covers an unload within the same frame: connecting then would hand
        // Chat 2 a gate name that no longer answers.
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            if (_disposed)
                return;

            RebuildSnapshot();
            TryConnect();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Plugin.Framework.Update -= OnFrameworkUpdate;
        Plugin.ClientState.Logout -= OnLogout;
        Plugin.ClientState.Login -= OnLoginStateChanged;
        _tabsChanged.Unsubscribe(OnTabsChanged);
        _available.Unsubscribe(OnAvailable);

        if (IsConnected && !_suspended)
        {
            try
            {
                _setProvider.InvokeAction(string.Empty);
            }
            catch
            {
                // Chat 2 already gone; nothing to unregister from.
            }
        }

        _provider.UnregisterFunc();
    }

    /// <summary>
    /// Call on the framework thread after any configuration change (Save/Apply, group membership
    /// edits) — rebuilds the decision snapshot and re-pushes the per-tab policies.
    /// </summary>
    public void SettingsChanged()
    {
        RebuildSnapshot();
        SendTabPolicies();
    }

    /// <summary>
    /// Stops providing styles while the Debug tester owns the (single-provider) gate. The tester's
    /// own registration already displaced ours in Chat 2; this only prevents an automatic
    /// re-registration (e.g. on ChatTwo.Available) until <see cref="Resume"/>.
    /// </summary>
    internal void Suspend() => _suspended = true;

    internal void Resume()
    {
        _suspended = false;
        TryConnect();
    }

    /// <summary>
    /// User-initiated disconnect (settings footer): unregisters the provider gate and blocks
    /// automatic reconnection (on <c>ChatTwo.Available</c>) until <see cref="Resume"/> — the
    /// footer's Connect action. Debug builds: the Debug tester's suspend/resume shares the same
    /// flag; acceptable dev-only overlap.
    /// </summary>
    internal void Disconnect()
    {
        _suspended = true;

        if (IsConnected)
        {
            try
            {
                _setProvider.InvokeAction(string.Empty);
            }
            catch
            {
                // Chat 2 already gone; nothing to unregister from.
            }
        }

        IsConnected = false;
    }

    private void OnAvailable() => TryConnect();

    // ChatListener already refreshes the friend-group lookup on login (shared instance, read
    // live); this only rebuilds the snapshot for the local player's name/worlds.
    private void OnLoginStateChanged() => RebuildSnapshot();

    private void OnLogout(int type, int code) => RebuildSnapshot();

    private void OnTabsChanged(Dictionary<Guid, string> tabs)
    {
        KnownTabs = new Dictionary<Guid, string>(tabs);
        PruneStalePolicies();
    }

    /// <summary>
    /// Refreshes the distance snapshot <see cref="EvaluateCore"/> reads, throttled to
    /// <see cref="DistanceRefreshIntervalMs"/> and only while the Chat 2 range styling can
    /// actually consume it — otherwise the cache is dropped so it can't go stale unnoticed.
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_snapshot is not { RangeEnabled: true } || !IsConnected || _suspended)
        {
            _playerDistances = null;
            _nextDistanceRefresh = 0;
            return;
        }

        var now = Environment.TickCount64;
        if (now < _nextDistanceRefresh)
            return;

        _nextDistanceRefresh = now + DistanceRefreshIntervalMs;
        _playerDistances = SenderDistance.Snapshot();
    }

    /// <summary>
    /// Drops per-tab policies for tabs Chat 2 no longer has. The tab list is authoritative
    /// whenever it arrives (GetTabs on connect, TabsChanged pushes the full list) and a deleted
    /// tab's id never comes back — without pruning, stale entries would sit in the config and be
    /// re-sent forever with no UI left to remove them.
    /// </summary>
    private void PruneStalePolicies()
    {
        var stale = _config.ChatTwoTabPolicies.Keys.Where(id => !KnownTabs.ContainsKey(id)).ToList();
        if (stale.Count == 0)
            return;

        foreach (var id in stale)
            _config.ChatTwoTabPolicies.Remove(id);

        _config.Save();
    }

    /// <summary>
    /// Probes Chat 2's styling IPC and (re-)registers the provider. Safe to call any time; a
    /// missing Chat 2 or an unsupported version just leaves <see cref="IsConnected"/> false.
    /// </summary>
    internal void TryConnect()
    {
        try
        {
            IsConnected = _styleVersion.InvokeFunc() == SupportedStyleVersion;
        }
        catch
        {
            IsConnected = false;
        }

        if (!IsConnected || _suspended)
            return;

        try
        {
            _setProvider.InvokeAction(ProviderGateName);
            KnownTabs = new Dictionary<Guid, string>(_getTabs.InvokeFunc());
            PruneStalePolicies();
            SendTabPolicies();
            _evaluateErrorLogged = false;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Plugin.Log.Warning(ex, "Registering with Chat 2's styling IPC failed");
        }
    }

    private void SendTabPolicies()
    {
        if (!IsConnected || _suspended)
            return;

        try
        {
            _setTabPolicies.InvokeAction(new Dictionary<Guid, int>(_config.ChatTwoTabPolicies));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Sending tab style policies to Chat 2 failed");
        }
    }

    /// <summary>
    /// Snapshot everything Evaluate needs. Group member lists are copied (GroupMembershipActions
    /// mutates the live config lists on the framework thread); the mention segmenter is only built
    /// when the range filter's mention bypass can actually trigger.
    /// </summary>
    private void RebuildSnapshot()
    {
        var rules = new List<GroupRule>(_config.Groups.Count + _config.FriendGroups.Count);
        var backgrounds = new Dictionary<string, uint>(rules.Capacity);

        foreach (var group in _config.Groups)
        {
            rules.Add(new GroupRule(group.Id, group.Active, FfGroup: null, [.. group.Members]));
            backgrounds[group.Id] = group.ChatTwoBackground;
        }

        foreach (var group in _config.FriendGroups.OrderBy(g => g.FfGroup))
        {
            rules.Add(new GroupRule(group.Id, group.Active, group.FfGroup, Members: []));
            backgrounds[group.Id] = group.ChatTwoBackground;
        }

        var rangeActive = _config.RangeFilterEnabled
            && (_config.RangeFilterChatTwoFade || _config.RangeFilterChatTwoHide);
        var wantMentions = rangeActive && _config.RangeFilterMentionsIgnoreRange;

        var loaded = Plugin.PlayerState.IsLoaded;
        _snapshot = new Snapshot(
            RangeEnabled: rangeActive,
            FadeOut: _config.RangeFilterFadeOut,
            CutOff: _config.RangeFilterCutOff,
            ChatTwoFade: _config.RangeFilterChatTwoFade,
            ChatTwoHide: _config.RangeFilterChatTwoHide,
            RangeChannels: [.. _config.RangeFilterChannels.Select(c => (ushort)c)],
            GroupRules: rules,
            GroupBackgrounds: backgrounds,
            MentionSegmenter: wantMentions
                ? new MessageSegmenter((IReadOnlyList<TokenRule>)[], ChatListener.BuildMentionRules(_config))
                : null,
            LocalName: loaded ? Plugin.PlayerState.CharacterName : string.Empty,
            LocalHomeWorld: loaded ? Plugin.PlayerState.HomeWorld.ValueNullable?.Name.ExtractText() : null,
            LocalCurrentWorld: loaded ? Plugin.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText() : null);
    }

    /// <summary>
    /// The provider gate: one call per message Chat 2 ingests, on its processing thread. Any
    /// failure degrades to unstyled — Chat 2 additionally guards the call, but a broken style
    /// provider must never affect chat rendering.
    /// </summary>
    private (uint Background, float Alpha) Evaluate(
        string senderName, string senderWorld, ulong contentId, ushort chatType,
        string senderRaw, string contentText)
    {
        try
        {
            return EvaluateCore(senderName, senderWorld, chatType, senderRaw, contentText);
        }
        catch (Exception ex)
        {
            if (!_evaluateErrorLogged)
            {
                _evaluateErrorLogged = true;
                Plugin.Log.Warning(ex, "Chat 2 style evaluation failed; messages render unstyled until reconnect/settings change");
            }

            return (0, 1f);
        }
    }

    private (uint Background, float Alpha) EvaluateCore(
        string senderName, string senderWorld, ushort chatType, string senderRaw, string contentText)
    {
        var snapshot = _snapshot;
        if (snapshot == null)
            return (0, 1f);

        // Same identity completion as ChatListener.ResolveWorldlessSender: senderName/-World come
        // from the sender's PlayerPayload and are empty for the local player's own posts (no
        // payload; senderRaw may carry a party-number prefix). Other world-less senders stand on
        // the current world.
        var name = senderName;
        var world = senderWorld.Length > 0 ? senderWorld : null;
        if (name.Length == 0)
        {
            if (snapshot.LocalName.Length > 0 && senderRaw.Contains(snapshot.LocalName, StringComparison.Ordinal))
            {
                name = snapshot.LocalName;
                world ??= snapshot.LocalHomeWorld;
            }
            else
            {
                name = senderRaw;
            }
        }

        world ??= snapshot.LocalCurrentWorld;

        var background = 0u;
        if (name.Length > 0 && !ChatListener.GroupingExcludedChannels.Contains((XivChatType)chatType))
        {
            // World-qualified friend lookups never touch IPlayerState; skip when unknown so the
            // lookup's internal current-world fallback can't run off the framework thread.
            var friendGroupIndex = world != null && _friendGroups.TryGetFriendGroupIndex(name, world, out var index)
                ? index
                : (int?)null;

            var groupId = GroupMatcher.FindGroup(name, world, friendGroupIndex, snapshot.GroupRules);
            if (groupId != null)
                background = snapshot.GroupBackgrounds.GetValueOrDefault(groupId);
        }

        if (!snapshot.RangeEnabled || !snapshot.RangeChannels.Contains(chatType))
            return (background, 1f);

        // Lock-free read of the framework-tick distance snapshot; null until the first refresh
        // after connecting. An unknown sender stays visible — the app's deliberate rule.
        var players = _playerDistances;
        if (players == null || SenderDistance.ResolveFrom(players, name, world) is not { } resolvedDistance)
            return (background, 1f);

        var visibility = RangeFade.CalculateVisibility(resolvedDistance, snapshot.FadeOut, snapshot.CutOff);
        if (visibility == RangeFade.MaxVisibility)
            return (background, 1f);

        if (snapshot.MentionSegmenter?.Segment([contentText])?.HasMention == true)
            return (background, 1f);

        if (visibility == 0)
        {
            if (snapshot.ChatTwoHide)
                return (background, 0f);

            return (background, snapshot.ChatTwoFade ? HiddenFallbackAlpha : 1f);
        }

        return (background, snapshot.ChatTwoFade ? visibility / (float)RangeFade.MaxVisibility : 1f);
    }
}
