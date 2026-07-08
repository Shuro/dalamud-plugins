using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Plugin;
using Newtonsoft.Json.Linq;

namespace GobchatEx.Chat;

/// <summary>
/// Best-effort read of Chat 2's own persisted per-channel colors (its "Chat colours" page),
/// straight off Chat 2's config file on disk — no IPC involved. GEX's existing custom Chat 2
/// styling IPC (background/alpha, <see cref="ChatTwoStyleProvider"/>) was built as a set of PRs
/// against the Chat 2 project that were never merged upstream; a second custom IPC for colors
/// would have the same fate, and most real installs run stock Chat 2 without it. Reading Chat 2's
/// own config file needs no cooperation from Chat 2 at all — every Dalamud plugin persists via
/// <c>IDalamudPluginInterface.SavePluginConfig</c>, which writes a flat
/// <c>&lt;InternalName&gt;.json</c> into a shared <c>pluginConfigs</c> folder that GEX's own
/// <c>ConfigFile.DirectoryName</c> already points at.
/// <para>
/// Chat 2's <c>ChatType</c> enum uses the same names and numeric values as <see
/// cref="XivChatType"/> for every channel GEX tracks (Say=10, Shout=11, CustomEmote=28,
/// StandardEmote=29, Yell=30), so JSON property names parse directly via <see
/// cref="Enum.TryParse{XivChatType}"/> — no translation table. Its packed color format
/// (red&lt;&lt;24 | green&lt;&lt;16 | blue&lt;&lt;8 | alpha) is byte-for-byte the same 0xRRGGBBAA
/// config-storage format this plugin's own <see cref="Core.RgbaColor"/> already uses, so no
/// repacking is needed either.
/// </para>
/// This is reading another plugin's private, unversioned persistence format by convention, not by
/// contract: a channel absent from Chat 2's own config means "using Chat 2's compiled-in default",
/// which isn't persisted anywhere readable, so it's simply absent from the result here too
/// (callers fall back to their own default). The whole read degrades to an empty result on any
/// failure — missing file, malformed JSON, a future Chat 2 schema change — rather than ever
/// affecting GEX's own message pipeline.
/// <para>
/// The config file persists across sessions regardless of whether Chat 2 is currently loaded —
/// uninstalling or disabling Chat 2 doesn't delete it — so a stale file alone must never be
/// treated as "Chat 2 is active right now". <see cref="IDalamudPluginInterface.InstalledPlugins"/>
/// (stock Dalamud, not the unmerged fork IPC) gives a live, reliable loaded/not-loaded signal via
/// <see cref="IExposedPlugin.IsLoaded"/>, gated on before even opening the file.
/// </para>
/// </summary>
internal static class ChatTwoChannelColors
{
    private const string ChatTwoInternalName = "ChatTwo";
    private const string ChatTwoConfigFileName = ChatTwoInternalName + ".json";

    internal static Dictionary<XivChatType, uint> Read()
    {
        if (!IsChatTwoLoaded())
            return [];

        var directory = Plugin.PluginInterface.ConfigFile.DirectoryName;
        if (directory == null)
            return [];

        var path = Path.Combine(directory, ChatTwoConfigFileName);
        if (!File.Exists(path))
            return [];

        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            if (root["ChatColours"] is not JObject chatColours)
                return [];

            var result = new Dictionary<XivChatType, uint>();
            foreach (var property in chatColours.Properties())
            {
                if (property.Value.Type == JTokenType.Integer
                    && Enum.TryParse<XivChatType>(property.Name, out var channel))
                    result[channel] = (uint)property.Value;
            }

            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to read Chat 2's own channel colors from {Path}; falling back to vanilla colors", path);
            return [];
        }
    }

    // Stock IDalamudPluginInterface.InstalledPlugins, not the unmerged styling IPC's IsConnected
    // handshake -- reliable regardless of whether Chat 2 has the custom fork PRs at all, since it
    // only asks Dalamud "is a plugin named ChatTwo currently loaded", nothing Chat 2-specific.
    private static bool IsChatTwoLoaded()
        => Plugin.PluginInterface.InstalledPlugins.Any(plugin => plugin.InternalName == ChatTwoInternalName && plugin.IsLoaded);
}
