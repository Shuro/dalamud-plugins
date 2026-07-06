using System;
using System.Collections.Generic;

namespace GobchatEx.Config;

/// <summary>Chat 2 tab settings (Milestone 3.5), persisted to tabs.json.</summary>
[Serializable]
public class TabsConfig
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Per-Chat 2-tab styling suppression flags (1 = no backgrounds, 2 = no fade, 4 = no hide),
    /// keyed by Chat 2's persistent tab identifier; tabs without an entry get everything.
    /// </summary>
    public Dictionary<Guid, int> ChatTwoTabPolicies { get; set; } = [];
}
