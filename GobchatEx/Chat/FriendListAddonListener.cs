using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace GobchatEx.Chat;

/// <summary>
/// Keeps <see cref="FriendGroupLookup"/> fresh without waiting for the next login, via Dalamud's
/// generic IAddonLifecycle service (no Dalamud service targets <c>InfoProxyFriendList</c> directly —
/// see <see cref="FriendGroupLookup"/>'s own doc comment). Watches the "FriendList" addon for
/// <see cref="AddonEvent.PostRequestedUpdate"/> — confirmed live (in-game log capture) to fire exactly
/// when the game pushes a real data change to the list, and only then: pressing Apply after actually
/// picking a different group in the "FriendGroupEdit" popup fires it, while Cancel and a no-op Apply
/// (re-picking the same group) do not. The popup's own lifecycle events were tried first
/// (PostReceiveEvent, PreFinalize, PostHide) but all three fire identically regardless of whether
/// anything changed — PostReceiveEvent even fires while just highlighting options before committing —
/// so they were dropped in favor of this one accurate signal.
/// </summary>
internal sealed class FriendListAddonListener : IDisposable
{
    private const string FriendListAddon = "FriendList";

    private readonly FriendGroupLookup _friendGroups;

    public FriendListAddonListener(FriendGroupLookup friendGroups)
    {
        _friendGroups = friendGroups;

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, FriendListAddon, OnAddonEvent);
    }

    public void Dispose()
        => Plugin.AddonLifecycle.UnregisterListener(OnAddonEvent);

    private void OnAddonEvent(AddonEvent type, AddonArgs args)
    {
        _friendGroups.Refresh();
        Plugin.Log.Information($"{args.AddonName} fired {type}; refreshed friend group lookup.");
    }
}
