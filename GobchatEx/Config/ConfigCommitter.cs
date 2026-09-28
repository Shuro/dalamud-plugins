using System;

namespace GobchatEx.Config;

/// <summary>
/// The single write path for the live <see cref="Configuration"/>: every writer (settings tabs,
/// Quickbar toggles, chat commands, context-menu group actions, the changelog, Chat 2 tab pruning)
/// mutates the section objects in place and then calls <see cref="CommitIfChanged"/> — never a
/// direct save, which would move a section past its snapshot and swallow another writer's pending
/// edit to it. Change detection compares per-section JSON against the last-persisted snapshot, so only changed section
/// files are written and <see cref="Changed"/> fires at most once per commit. Framework thread only.
/// </summary>
internal sealed class ConfigCommitter
{
    private readonly Configuration _config;
    private readonly string[] _persisted;

    public ConfigCommitter(Configuration config)
    {
        _config = config;
        var sections = config.Sections;
        _persisted = new string[sections.Length];
        for (var i = 0; i < sections.Length; i++)
            _persisted[i] = Configuration.Serialize(sections[i].Section);
    }

    /// <summary>Raised after a commit wrote at least one section: rebuild whatever derives from config.</summary>
    public event Action? Changed;

    /// <summary>
    /// Persists every section whose JSON differs from its last-persisted snapshot, then raises
    /// <see cref="Changed"/> once. The snapshot advances even if a disk write failed
    /// (<see cref="Configuration.SaveSection"/> logs and swallows I/O errors) — the in-memory
    /// state is what the consumers apply either way.
    /// </summary>
    public void CommitIfChanged()
    {
        var sections = _config.Sections;
        var changed = false;

        for (var i = 0; i < sections.Length; i++)
        {
            var json = Configuration.Serialize(sections[i].Section);
            if (json == _persisted[i])
                continue;

            Configuration.SaveSection(sections[i].FileName, json);
            _persisted[i] = json;
            changed = true;
        }

        if (changed)
            Changed?.Invoke();
    }
}
