using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Ipc;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Draws the same "add/remove from group" toggles as the native right-click menu
/// (<see cref="Plugin.OnMenuOpened"/> via <c>IContextMenu</c>), but inside Chat 2's own ImGui-drawn
/// context menu via its plugin-integration IPC (see <c>.reference/ChatTwo/ipc.md</c>). Chat 2 draws its
/// context menu entirely inside its own window and never fires the native <c>IContextMenu</c> hook, so
/// this is a separate integration surface, not a duplicate of the native one.
/// <para>
/// No explicit "is Chat 2 installed" check exists or is needed: if Chat 2 is never installed or gets
/// disabled, <see cref="invoke"/> simply never fires (nothing on the other end calls the IPC), so the
/// integration is inert by construction — exactly the doc's own intended usage pattern. Registration
/// calls are wrapped in try/catch because <c>InvokeFunc()</c>/<c>InvokeAction()</c> throw when the IPC
/// provider doesn't exist yet; <see cref="available"/> re-registers if/when Chat 2 loads afterward.
/// </para>
/// </summary>
internal sealed class ChatTwoContextMenuIntegration : IDisposable
{
    private readonly Plugin plugin;
    private readonly ICallGateSubscriber<string> register;
    private readonly ICallGateSubscriber<string, object?> unregister;
    private readonly ICallGateSubscriber<object?> available;
    private readonly ICallGateSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?> invoke;

    private string? id;

    public ChatTwoContextMenuIntegration(Plugin plugin)
    {
        this.plugin = plugin;

        register = Plugin.PluginInterface.GetIpcSubscriber<string>("ChatTwo.Register");
        unregister = Plugin.PluginInterface.GetIpcSubscriber<string, object?>("ChatTwo.Unregister");
        available = Plugin.PluginInterface.GetIpcSubscriber<object?>("ChatTwo.Available");
        invoke = Plugin.PluginInterface
            .GetIpcSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?>("ChatTwo.Invoke");

        available.Subscribe(TryRegister);
        TryRegister();
        invoke.Subscribe(OnInvoke);
    }

    public void Dispose()
    {
        invoke.Unsubscribe(OnInvoke);
        available.Unsubscribe(TryRegister);

        if (id != null)
        {
            try
            {
                unregister.InvokeAction(id);
            }
            catch
            {
                // Chat 2 unloaded before we did; nothing left to unregister.
            }

            id = null;
        }
    }

    private void TryRegister()
    {
        try
        {
            id = register.InvokeFunc();
        }
        catch
        {
            // Chat 2 isn't installed/loaded yet; Available fires later if/when it is.
        }
    }

    private void OnInvoke(
        string invokedId,
        PlayerPayload? sender,
        ulong contentId,
        Payload? payload,
        SeString? senderString,
        SeString? content)
    {
        if (invokedId != id)
            return;

        // "payload" is whichever payload the user actually right-clicked (ipc.md: "the payload that was
        // right-clicked, if any"). For an emote like "Saya looks at Ghosty in surprise.", right-clicking
        // "Ghosty" must target Ghosty, not the message's sender — so a clicked PlayerPayload always wins
        // over the sender. Only when nothing specific was clicked (plain text, or the sender's own name/
        // area) do we fall back to resolving the message sender itself, the same way the native path
        // does via SenderIdentity (Chat 2's own "sender" extraction is frequently null in practice, so we
        // resolve from the raw senderString rather than trusting it).
        string name;
        string? world;
        if (payload is PlayerPayload clickedPlayer)
        {
            name = clickedPlayer.PlayerName;
            world = clickedPlayer.World.ValueNullable?.Name.ExtractText();
        }
        else if (senderString is not null)
        {
            SenderIdentity.Resolve(senderString, out name, out world);
        }
        else
        {
            return;
        }

        if (name.Length == 0)
            return;

        // A same-world sender's raw text carries no world at all (no PlayerPayload, no cross-world
        // icon — there's nothing to display), so the senderString fallback above can only resolve the
        // name. Default to the viewer's own current world so this produces the same trigger key as
        // right-clicking a fully-qualified name link for the same player, instead of silently falling
        // back to a cross-world-agnostic bare-name trigger.
        world ??= Plugin.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText();

        // Chat 2 draws every registered integration flat inside its own "Integrations" menu, so nest
        // our own items one level deeper under a "GobchatEx" submenu rather than mixing them in with
        // whatever other plugins also register.
        using var menu = ImRaii.Menu("GobchatEx");
        if (!menu.Success)
            return;

        if (plugin.Configuration.Groups.Groups.Count == 0)
        {
            using (ImRaii.Disabled(true))
                ImGui.Selectable(Loc.Get("Groups_ContextMenu_None"));
            return;
        }

        var actions = new GroupMembershipActions(plugin, name, world);

        foreach (var group in plugin.Configuration.Groups.Groups)
        {
            var inGroup = actions.IsInGroup(group);
            var label = inGroup
                ? string.Format(Loc.Get("Groups_ContextMenu_RemoveFrom"), group.Name)
                : string.Format(Loc.Get("Groups_ContextMenu_AddTo"), group.Name);

            if (!ImGui.Selectable(label))
                continue;

            if (inGroup)
                actions.RemoveFromGroup(group);
            else
                actions.AddToGroup(group);
        }
    }
}
