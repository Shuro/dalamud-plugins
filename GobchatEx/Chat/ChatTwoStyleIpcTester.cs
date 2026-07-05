#if DEBUG
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Ipc;
using GobchatEx.Core;

namespace GobchatEx.Chat;

/// <summary>
/// Dev-only exerciser for Chat 2's message styling IPC (<c>ChatTwo.StyleVersion</c>,
/// <c>ChatTwo.SetMessageStyleProvider</c>, <c>ChatTwo.GetTabs</c>, <c>ChatTwo.TabsChanged</c>,
/// <c>ChatTwo.SetTabStylePolicies</c> — see ipc.md in the ChatTwo fork), driven from the settings
/// window's Debug page. Holds one configurable test rule that the provider gate applies to incoming
/// messages, and records every provider invocation for inspection.
/// <para>
/// Chat 2 calls the provider on its message processing thread, so the invocation log and counters
/// are guarded by a lock. The rule fields are deliberately unguarded: primitives written from the
/// draw thread and only read by the provider — a transiently stale value is acceptable in a debug
/// tool.
/// </para>
/// </summary>
internal sealed class ChatTwoStyleIpcTester : IDisposable
{
    internal const string ProviderGateName = "GobchatEx.DebugMessageStyle";

    // Suppress-flags understood by ChatTwo.SetTabStylePolicies — canonical values live on the
    // production provider; aliased here so the Debug tab keeps reading them off the tester.
    internal const int SuppressBackground = ChatTwoStyleProvider.SuppressBackground;
    internal const int SuppressFade = ChatTwoStyleProvider.SuppressFade;
    internal const int SuppressHide = ChatTwoStyleProvider.SuppressHide;

    private const int MaxLoggedInvocations = 30;

    internal sealed record Invocation(
        DateTime Time,
        string SenderName,
        string SenderWorld,
        ulong ContentId,
        ushort ChatType,
        string SenderRaw,
        string ContentText,
        uint ReturnedBackground,
        float ReturnedAlpha);

    // Test rule applied by the provider; edited live from the Debug tab.
    internal bool RuleEnabled;
    internal string RuleNameFilter = string.Empty;
    internal int RuleChatTypeFilter = -1;
    internal bool RuleApplyBackground = true;
    internal Vector4 RuleBackground = new(0f, 0.5f, 1f, 0.25f);
    internal float RuleAlpha = 1f;

    internal bool AutoReregister = true;
    internal bool Registered { get; private set; }
    internal int? LastVersion { get; private set; }
    internal string? LastError { get; private set; }
    internal int AvailableCount { get; private set; }
    internal int TabsChangedCount { get; private set; }
    internal DateTime? LastTabsChangedAt { get; private set; }
    internal Dictionary<Guid, string> KnownTabs { get; private set; } = [];
    internal Dictionary<Guid, int> PolicyDraft { get; } = [];
    internal int InvocationCount { get; private set; }

    private readonly ICallGateSubscriber<int> styleVersion;
    private readonly ICallGateSubscriber<string, object?> setProvider;
    private readonly ICallGateSubscriber<object?> available;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>> getTabs;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>, object?> tabsChanged;
    private readonly ICallGateSubscriber<Dictionary<Guid, int>, object?> setTabPolicies;
    private readonly ICallGateProvider<string, string, ulong, ushort, string, string, (uint, float)> provider;

    private readonly object sync = new();
    private readonly Queue<Invocation> invocations = new();

    public ChatTwoStyleIpcTester()
    {
        styleVersion = Plugin.PluginInterface.GetIpcSubscriber<int>("ChatTwo.StyleVersion");
        setProvider = Plugin.PluginInterface.GetIpcSubscriber<string, object?>("ChatTwo.SetMessageStyleProvider");
        available = Plugin.PluginInterface.GetIpcSubscriber<object?>("ChatTwo.Available");
        getTabs = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("ChatTwo.GetTabs");
        tabsChanged = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>, object?>("ChatTwo.TabsChanged");
        setTabPolicies = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, int>, object?>("ChatTwo.SetTabStylePolicies");

        provider = Plugin.PluginInterface
            .GetIpcProvider<string, string, ulong, ushort, string, string, (uint, float)>(ProviderGateName);
        provider.RegisterFunc(GetStyle);

        available.Subscribe(OnAvailable);
        tabsChanged.Subscribe(OnTabsChanged);
    }

    public void Dispose()
    {
        tabsChanged.Unsubscribe(OnTabsChanged);
        available.Unsubscribe(OnAvailable);

        if (Registered)
            Unregister();

        provider.UnregisterFunc();
    }

    internal void QueryVersion()
    {
        try
        {
            LastVersion = styleVersion.InvokeFunc();
            LastError = null;
        }
        catch (Exception ex)
        {
            LastVersion = null;
            LastError = ex.Message;
        }
    }

    /// <summary>
    /// The production provider (<see cref="ChatTwoStyleProvider"/>), when one exists. Chat 2's
    /// gate is single-provider (last writer wins), so the tester suspends it while registered
    /// and resumes it on unregister — set by Plugin once both are constructed.
    /// </summary>
    internal ChatTwoStyleProvider? ProductionProvider { get; set; }

    internal void Register()
    {
        // Suspend first: registering below displaces the production provider in Chat 2 anyway;
        // this stops it from re-registering (e.g. on ChatTwo.Available) while the tester tests.
        ProductionProvider?.Suspend();

        try
        {
            setProvider.InvokeAction(ProviderGateName);
            Registered = true;
            LastError = null;
        }
        catch (Exception ex)
        {
            Registered = false;
            LastError = ex.Message;
        }
    }

    internal void Unregister()
    {
        try
        {
            setProvider.InvokeAction(string.Empty);
            LastError = null;
        }
        catch (Exception ex)
        {
            // Chat 2 unloaded before we did; nothing left to unregister.
            LastError = ex.Message;
        }

        Registered = false;
        ProductionProvider?.Resume();
    }

    internal void RefreshTabs()
    {
        try
        {
            KnownTabs = getTabs.InvokeFunc();
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    internal void SendPolicies()
    {
        try
        {
            setTabPolicies.InvokeAction(new Dictionary<Guid, int>(PolicyDraft));
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    internal Invocation[] SnapshotInvocations()
    {
        lock (sync)
            return invocations.ToArray();
    }

    internal void ClearInvocations()
    {
        lock (sync)
        {
            invocations.Clear();
            InvocationCount = 0;
        }
    }

    private void OnAvailable()
    {
        AvailableCount++;

        // Chat 2 (re)loaded, which wipes its provider registration. Mirror the
        // re-register-on-Available pattern a real integration would use.
        if (AutoReregister && Registered)
            Register();
    }

    private void OnTabsChanged(Dictionary<Guid, string> tabs)
    {
        KnownTabs = tabs;
        TabsChangedCount++;
        LastTabsChangedAt = DateTime.Now;
    }

    private (uint, float) GetStyle(
        string senderName,
        string senderWorld,
        ulong contentId,
        ushort chatType,
        string senderRaw,
        string contentText)
    {
        var background = 0u;
        var alpha = 1f;

        if (RuleEnabled
            && (RuleChatTypeFilter < 0 || chatType == RuleChatTypeFilter)
            && (RuleNameFilter.Length == 0
                || senderName.Contains(RuleNameFilter, StringComparison.OrdinalIgnoreCase)
                || senderRaw.Contains(RuleNameFilter, StringComparison.OrdinalIgnoreCase)))
        {
            if (RuleApplyBackground)
                background = RgbaColor.FromVector4(RuleBackground);
            alpha = RuleAlpha;
        }

        lock (sync)
        {
            invocations.Enqueue(new Invocation(
                DateTime.Now, senderName, senderWorld, contentId, chatType, senderRaw, contentText,
                background, alpha));
            while (invocations.Count > MaxLoggedInvocations)
                invocations.Dequeue();
            InvocationCount++;
        }

        return (background, alpha);
    }
}
#endif
