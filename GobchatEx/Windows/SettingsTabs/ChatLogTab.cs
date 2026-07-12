using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Chat logging settings (Milestone 5): start/stop button with a live status line, the log folder
/// (no default — the start button stays disabled until the user picks one), the per-character
/// subfolder toggle, and the logged-channel grids. The button drives the <see cref="ChatLogger"/>'s
/// session-scoped state directly (immediate, not routed through the debounced config commit —
/// logging is a runtime action, not a persisted setting, hence also no nav-rail switch); the
/// folder and channel settings below are ordinary instant-apply config.
/// </summary>
internal sealed class ChatLogTab : ISettingsTab
{
    public string Name => Loc.Get("ChatLog_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.FileAlt;

    private readonly ChatLogConfig config;
    private readonly ChatLogger logger;
    private readonly FileDialogManager fileDialog = new();

    public ChatLogTab(ChatLogConfig config, ChatLogger logger)
    {
        this.config = config;
        this.logger = logger;
    }

    public void Draw()
    {
        DrawStatus();

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("ChatLog_Folder_Header"), Loc.Get("ChatLog_Folder_Tooltip"));
        DrawFolder();

        ImGuiHelpers.ScaledDummy(10f);
        SettingsUi.SectionHeader(Loc.Get("ChatLog_Channels_Header"), Loc.Get("ChatLog_Channels_Tooltip"));
        DrawChannels();

        fileDialog.Draw();
    }

    private void DrawStatus()
    {
        var running = logger.IsLogging;
        var missingFolder = !running && !logger.HasLogFolder;
        using (ImRaii.Disabled(missingFolder))
        {
            if (ImGuiComponents.IconButtonWithText(
                    running ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play,
                    Loc.Get(running ? "ChatLog_Button_Stop" : "ChatLog_Button_Start")))
            {
                if (running)
                    logger.StopLogging();
                else
                    logger.StartLogging();
            }
        }

        SettingsUi.Tooltip(Loc.Get(missingFolder
            ? "ChatLog_Button_NoFolder_Tooltip"
            : "ChatLog_Button_StartStop_Tooltip"));

        ImGui.SameLine();
        if (missingFolder)
            ImGui.TextColored(ImGuiColors.DalamudOrange, Loc.Get("ChatLog_Status_NoFolder"));
        else if (!running)
            ImGui.TextDisabled(Loc.Get("ChatLog_Status_Inactive"));
        else if (logger.CurrentFilePath is { } path)
        {
            ImGui.TextColored(ImGuiColors.HealerGreen, Loc.Get("ChatLog_Status_Active"));
            ImGui.TextWrapped(string.Format(Loc.Get("ChatLog_Status_CurrentFile"), path));
        }
        else
            ImGui.TextColored(ImGuiColors.HealerGreen, Loc.Get("ChatLog_Status_WaitingForMessage"));
    }

    private void DrawFolder()
    {
        var path = config.LogFolder;
        var reserved = SettingsUi.IconButtonWidth(FontAwesomeIcon.FolderOpen)
            + SettingsUi.IconButtonWidth(FontAwesomeIcon.Undo)
            + ImGui.GetStyle().ItemSpacing.X * 2f;
        ImGui.SetNextItemWidth(-reserved);
        if (ImGui.InputTextWithHint("##logFolder", Loc.Get("ChatLog_Folder_Hint"), ref path, 260))
            config.LogFolder = path;

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            fileDialog.OpenFolderDialog(Loc.Get("ChatLog_Folder_PickerTitle"),
                (ok, folder) =>
                {
                    if (ok)
                        config.LogFolder = folder;
                },
                Directory.Exists(logger.ResolvedLogFolder) ? logger.ResolvedLogFolder : null);
        }

        SettingsUi.Tooltip(Loc.Get("ChatLog_Folder_Browse_Tooltip"));

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
            config.LogFolder = string.Empty;
        SettingsUi.Tooltip(Loc.Get("ChatLog_Folder_Reset_Tooltip"));

        // The applied folder, refreshed by the commit tick — shows where files actually land.
        // Wrapped instead of clipped: users read the full path off this line. Hidden while no
        // usable folder is configured — the status line next to the start button explains why.
        if (logger.HasLogFolder)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled)))
                ImGui.TextWrapped(string.Format(Loc.Get("ChatLog_Folder_Resolved"), logger.ResolvedLogFolder));
        }

        if (logger.LogFolderInvalid)
            SettingsUi.Warning(Loc.Get("ChatLog_Folder_InvalidPath"));

        ImGuiHelpers.ScaledDummy(6f);
        var perCharacter = config.UseCharacterFolders;
        if (SettingsUi.Toggle(Loc.Get("ChatLog_CharacterFolders"), ref perCharacter))
            config.UseCharacterFolders = perCharacter;
        ImGuiComponents.HelpMarker(Loc.Get("ChatLog_CharacterFolders_Tooltip"));
    }

    private void DrawChannels()
    {
        SettingsUi.ChannelGrid("##chatlog-main", FormattingTab.MainChannels, config.LogChannels);

        if (ImGui.CollapsingHeader(Loc.Get("Formatting_Channels_Linkshells")))
            SettingsUi.ChannelGrid("##chatlog-ls", FormattingTab.LinkshellChannels, config.LogChannels);

        if (ImGui.CollapsingHeader(Loc.Get("Formatting_Channels_CrossworldLinkshells")))
            SettingsUi.ChannelGrid("##chatlog-cwls", FormattingTab.CrossworldLinkshellChannels, config.LogChannels);
    }
}
