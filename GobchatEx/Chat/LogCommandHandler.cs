using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Chat;

/// <summary>
/// Parses "/gex log ..." (stripped by <see cref="CommandDispatcher"/> before this is called):
/// "start"/"stop" drive <see cref="ChatLogger.StartLogging"/>/<see cref="ChatLogger.StopLogging"/> —
/// the same session-scoped manual action as the Logs tab and Quickbar buttons, so the state they
/// display stays in sync automatically — and "status" reports the current state. Logging still
/// cannot start without a user-chosen log folder (there is no default), and nothing here is
/// persisted: the command mirrors the buttons exactly. ChatLogger is framework-thread-only, which
/// is safe here for the same reason as <see cref="PlayerCommandHandler"/> — command callbacks
/// already run on the framework thread. The "start"/"stop"/"status" verb parsing itself lives in
/// <see cref="LogCommandVerbParser"/> (Dalamud-free, unit tested).
/// </summary>
internal static class LogCommandHandler
{
    public static void Execute(Plugin plugin, string args)
    {
        switch (LogCommandVerbParser.Parse(args))
        {
            case LogCommandVerbKind.Start:
                ExecuteStart(plugin);
                break;
            case LogCommandVerbKind.Stop:
                ExecuteStop(plugin);
                break;
            case LogCommandVerbKind.Status:
                ExecuteStatus(plugin);
                break;
            case LogCommandVerbKind.Invalid:
                Plugin.ChatGui.PrintError(Loc.Get("Commands_Log_InvalidSyntax"));
                break;
        }
    }

    private static void ExecuteStart(Plugin plugin)
    {
        var logger = plugin.ChatLogger;
        if (!logger.HasLogFolder)
        {
            Plugin.ChatGui.PrintError(Loc.Get("Commands_Log_NoFolder"));
            return;
        }

        if (logger.IsLogging)
        {
            Plugin.ChatGui.Print(Loc.Get("Commands_Log_AlreadyLogging"));
            return;
        }

        logger.StartLogging();
        Plugin.ChatGui.Print(Loc.Get("Commands_Log_Started"));
    }

    private static void ExecuteStop(Plugin plugin)
    {
        var logger = plugin.ChatLogger;
        if (!logger.IsLogging)
        {
            Plugin.ChatGui.Print(Loc.Get("Commands_Log_NotLogging"));
            return;
        }

        logger.StopLogging();
        Plugin.ChatGui.Print(Loc.Get("Commands_Log_Stopped"));
    }

    private static void ExecuteStatus(Plugin plugin)
    {
        var logger = plugin.ChatLogger;
        if (!logger.IsLogging)
        {
            Plugin.ChatGui.Print(Loc.Get("Commands_Log_StatusOff"));
            return;
        }

        // The session file is created lazily with the first loggable message, so a fresh
        // session has no path to show yet — mirrors the Logs tab's "waiting" status line.
        Plugin.ChatGui.Print(logger.CurrentFilePath is { } path
            ? string.Format(Loc.Get("Commands_Log_StatusOnFile"), path)
            : Loc.Get("Commands_Log_StatusOn"));
    }
}
