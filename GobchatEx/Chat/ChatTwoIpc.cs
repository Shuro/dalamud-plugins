using System;
using System.Collections.Generic;
using Dalamud.Plugin.Ipc;

namespace GobchatEx.Chat;

/// <summary>
/// Chat 2's IPC contract in one place — gate names, signatures and flag values shared by the
/// production style provider, the Debug-build tester and the context-menu integration.
/// </summary>
internal static class ChatTwoIpc
{
    /// <summary>Chat 2's plugin internal name as Dalamud reports it (InstalledPlugins, ActivePluginsChanged).</summary>
    public const string InternalName = "ChatTwo";

    // Per-tab suppress-flags understood by ChatTwo.SetTabStylePolicies.
    public const int SuppressBackground = 1;
    public const int SuppressFade = 2;
    public const int SuppressHide = 4;

    public static ICallGateSubscriber<object?> Available()
        => Plugin.PluginInterface.GetIpcSubscriber<object?>("ChatTwo.Available");

    public static ICallGateSubscriber<int> StyleVersion()
        => Plugin.PluginInterface.GetIpcSubscriber<int>("ChatTwo.StyleVersion");

    public static ICallGateSubscriber<string, object?> SetMessageStyleProvider()
        => Plugin.PluginInterface.GetIpcSubscriber<string, object?>("ChatTwo.SetMessageStyleProvider");

    public static ICallGateSubscriber<Dictionary<Guid, string>> GetTabs()
        => Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("ChatTwo.GetTabs");

    public static ICallGateSubscriber<Dictionary<Guid, string>, object?> TabsChanged()
        => Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, string>, object?>("ChatTwo.TabsChanged");

    public static ICallGateSubscriber<Dictionary<Guid, int>, object?> SetTabStylePolicies()
        => Plugin.PluginInterface.GetIpcSubscriber<Dictionary<Guid, int>, object?>("ChatTwo.SetTabStylePolicies");

    /// <summary>
    /// A message-style provider gate Chat 2 calls once per message: (senderName, senderWorld,
    /// contentId, chatType, senderRaw, contentText) → (background RGBA, alpha).
    /// </summary>
    public static ICallGateProvider<string, string, ulong, ushort, string, string, (uint, float)> StyleProvider(string gateName)
        => Plugin.PluginInterface.GetIpcProvider<string, string, ulong, ushort, string, string, (uint, float)>(gateName);
}
