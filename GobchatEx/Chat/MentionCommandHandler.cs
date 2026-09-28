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
/// mutate the live config and commit through <see cref="Config.ConfigCommitter"/>. The
/// "add"/"remove"/"list" verb parsing
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
        if (triggers.Any(x => string.Equals(x.Word, word, StringComparison.OrdinalIgnoreCase)))
        {
            Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Mention_AlreadyExists"), word));
            return;
        }

        triggers.Add(new MentionTrigger { Word = word });
        plugin.ConfigCommitter.CommitIfChanged();
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
        if (triggers.RemoveAll(x => string.Equals(x.Word, word, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Mention_NotFound"), word));
            return;
        }

        plugin.ConfigCommitter.CommitIfChanged();
        Plugin.ChatGui.Print(string.Format(Loc.Get("Commands_Mention_Removed"), word));
    }

    private static void ExecuteList(Plugin plugin)
    {
        var triggers = plugin.Configuration.Mentions.MentionTriggers;
        Plugin.ChatGui.Print(triggers.Count == 0
            ? Loc.Get("Commands_Mention_ListEmpty")
            : string.Format(Loc.Get("Commands_Mention_List"), string.Join(", ", triggers.Select(t => t.Word))));
    }
}
