using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;

namespace GobchatEx.Chat;

/// <summary>
/// Snapshots the game's friend list into a (name, world) → friend-group-index lookup. No Dalamud
/// service wraps <c>InfoProxyFriendList</c> for plugins, so this reads FFXIVClientStructs directly;
/// failure (proxy not ready, world row missing) degrades to "no match" rather than throwing. Refreshed
/// on login, once at plugin load when already logged in (mid-session plugin update / dev auto-reload,
/// which never fire Login), live via <see cref="FriendListAddonListener"/> watching the game's own
/// FriendList addon, and — in Debug builds only — a manual button on the Debug page's Friend Groups
/// pane for dev iteration without relogging; never per message. Login and ImGui draw callbacks already
/// run on the framework thread; the plugin-load call site dispatches explicitly because plugin
/// construction isn't guaranteed to be framework-thread without LoadSync.
/// </summary>
internal sealed class FriendGroupLookup
{
    private Dictionary<(string Name, string World), int> _index = new();

    /// <summary>Read-only snapshot for the Debug page's Friend Groups pane; not for matching logic.</summary>
    public IReadOnlyDictionary<(string Name, string World), int> Entries => _index;

    public unsafe void Refresh()
    {
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
            return;

        var worldSheet = Plugin.DataManager.GetExcelSheet<World>();
        var next = new Dictionary<(string, string), int>();

        foreach (var character in proxy->CharDataSpan)
        {
            // None: no display group assigned; All only appears as a filter value, never per-entry.
            if (character.Group is InfoProxyCommonList.DisplayGroup.None or InfoProxyCommonList.DisplayGroup.All)
                continue;

            if (!worldSheet.TryGetRow(character.HomeWorld, out var worldRow))
                continue;

            var key = (character.NameString.ToLowerInvariant(), worldRow.Name.ExtractText().ToLowerInvariant());
            next[key] = (int)character.Group - 1; // Star=1..Club=7 -> ffgroup index 0..6
        }

        _index = next;
    }

    /// <summary>
    /// Same-world senders resolve with <paramref name="world"/> null (Dalamud omits the world suffix
    /// entirely for them); the friend list stores an absolute home world per entry regardless, so a
    /// null world falls back to the local player's own current world for the lookup key.
    /// </summary>
    public bool TryGetFriendGroupIndex(string name, string? world, out int index)
    {
        var worldName = world ?? Plugin.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrEmpty(worldName))
        {
            index = -1;
            return false;
        }

        return _index.TryGetValue((name.ToLowerInvariant(), worldName.ToLowerInvariant()), out index);
    }
}
