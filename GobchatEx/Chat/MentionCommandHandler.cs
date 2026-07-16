using System;
using System.Linq;
using GobchatEx.Config;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Parses "/gex mention ..." (stripped by <see cref="CommandDispatcher"/> before this is called):
/// "add"/"remove" edit <see cref="Config.MentionsConfig.MentionTriggers"/> — the same list the
/// Mentions tab's trigger editor mutates, with the same trim-and-case-insensitive-dedupe rule, so
/// command and tab always agree on what counts as a duplicate — and "list" prints them. Edits
/// persist and apply through the same path as <see cref="GroupMembershipActions"/>: mutate the
/// live config, save, notify both render pipelines; the settings window being open is fine (its
/// instant-apply commit would just re-save the same state). The "add"/"remove"/"list" verb parsing
/// itself lives in <see cref="MentionCommandVerbParser"/> (Dalamud-free, unit tested).
/// </summary>
internal static class MentionCommandHandler
{
    public static void Execute(Plugin plugin, string args)
    {
        var verb = MentionCommandVerbParser.Parse(args);

        switch (verb.Kind)
        {
            case MentionCommandVerbKind.Add:
                ExecuteAdd(plugin, verb.Rest);
                break;
            case MentionCommandVerbKind.Remove:
                ExecuteRemove(plugin, verb.Rest);
                break;
            case MentionCommandVerbKind.List:
                ExecuteList(plugin);
                break;
            case MentionCommandVerbKind.Invalid:
                Plugin.ChatGui.PrintError(Loc.Get("Commands_Mention_InvalidSyntax"));
                break;
        }
    }

    private static void ExecuteAdd(Plugin plugin, string rest)
    {
        var word = rest.Trim();
        if (word.Length == 0)
        {
            Plugin.ChatGui.PrintError(Loc.Get("Commands_Mention_InvalidSyntax"));
            return;
        }

        var triggers = plugin.Configuration.Mentions.MentionTriggers;
        if (triggers.Any(x => x.Word.Equals(word, StringComparison.OrdinalIgnoreCase)))
        {
            Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Mention_AlreadyExists"), word));
            return;
        }

        triggers.Add(new MentionTrigger { Word = word });
        Persist(plugin);
        Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Mention_Added"), word));
    }

    private static void ExecuteRemove(Plugin plugin, string rest)
    {
        var word = rest.Trim();
        if (word.Length == 0)
        {
            Plugin.ChatGui.PrintError(Loc.Get("Commands_Mention_InvalidSyntax"));
            return;
        }

        var triggers = plugin.Configuration.Mentions.MentionTriggers;
        if (triggers.RemoveAll(x => x.Word.Equals(word, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Mention_NotFound"), word));
            return;
        }

        Persist(plugin);
        Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Mention_Removed"), word));
    }

    private static void ExecuteList(Plugin plugin)
    {
        var triggers = plugin.Configuration.Mentions.MentionTriggers;
        Plugin.ChatGui.Print(triggers.Count == 0
            ? Loc.Get("Commands_Mention_ListEmpty")
            : string.Format(Loc.Get("Commands_Mention_List"), string.Join(", ", triggers.Select(t => t.Word))));
    }

    /// <summary>
    /// Same persist-and-apply path as <see cref="GroupMembershipActions"/>: the Chat 2 provider
    /// builds its mention-bypass segmenter from the same trigger list, so it must be notified
    /// alongside the native-log listener.
    /// </summary>
    private static void Persist(Plugin plugin)
    {
        plugin.Configuration.Save();
        plugin.ChatListener.SettingsChanged();
        plugin.ChatTwoStyles.SettingsChanged();
    }
}
