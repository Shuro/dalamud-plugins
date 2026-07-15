using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// The shared alert-sound editor — game-effect/custom-file source radio, effect combo with
/// instant preview, file path + browse + preview with missing/failed/too-long warnings, and an
/// optional volume slider — drawing against any <see cref="IAlertSoundSettings"/>. Extracted
/// from the Mentions tab so the Groups tab's per-group sounds (Milestone 6) reuse the same
/// widget instead of duplicating it. One instance serves many settings objects in the same tab:
/// the exists/duration probe is cached per distinct path (a per-frame File.Exists would hit the
/// disk while the tab is open) and a failed preview is remembered per path. Callers drawing it
/// more than once per frame must scope each call with their own PushId.
/// </summary>
internal sealed class AlertSoundEditor
{
    // Alert sounds should stay short; anything longer gets a warning (the
    // file still plays — it's advice, not a limit).
    private static readonly TimeSpan MaxAlertDuration = TimeSpan.FromSeconds(5);

    // Editing a path character by character leaves one probe per keystroke behind; wholesale
    // reset once the cap is hit, same reasoning as SoundPlayer's file cache.
    private const int MaxProbes = 64;

    private readonly FileDialogManager fileDialog;
    private readonly SoundPlayer soundPlayer;

    private sealed class PathProbe
    {
        public bool Exists;
        public TimeSpan? Duration;

        // A failed preview would otherwise be invisible (the error only lands
        // in the log). Sticks until a preview of this path succeeds.
        public bool PreviewFailed;
    }

    private readonly Dictionary<string, PathProbe> probes = new(StringComparer.OrdinalIgnoreCase);

    public AlertSoundEditor(FileDialogManager fileDialog, SoundPlayer soundPlayer)
    {
        this.fileDialog = fileDialog;
        this.soundPlayer = soundPlayer;
    }

    /// <summary>
    /// <paramref name="showVolume"/> draws the volume slider under the file row; the Mentions
    /// tab passes false and keeps its own two-up cooldown/volume layout instead.
    /// </summary>
    public void Draw(IAlertSoundSettings settings, bool showVolume)
    {
        if (ImGui.RadioButton(Loc.Get("Sound_SourceGame"), !settings.SoundUseCustomFile))
            settings.SoundUseCustomFile = false;
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.Get("Sound_SourceFile"), settings.SoundUseCustomFile))
            settings.SoundUseCustomFile = true;

        if (settings.SoundUseCustomFile)
            DrawCustomFile(settings, showVolume);
        else
            DrawGameEffect(settings);
    }

    private static void DrawGameEffect(IAlertSoundSettings settings)
    {
        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("##soundEffect", GameSound.Name(settings.SoundEffect)))
        {
            if (combo)
            {
                for (var effect = GameSound.Min; effect <= GameSound.Max; ++effect)
                {
                    if (!ImGui.Selectable(GameSound.Name(effect), effect == settings.SoundEffect))
                        continue;

                    settings.SoundEffect = effect;
                    SoundPlayer.Play(effect); // instant preview of the choice
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
            SoundPlayer.Play(settings.SoundEffect);
    }

    private void DrawCustomFile(IAlertSoundSettings settings, bool showVolume)
    {
        var path = settings.SoundFilePath;
        var reserved = SettingsUi.IconButtonWidth(FontAwesomeIcon.FolderOpen)
            + SettingsUi.IconButtonWidth(FontAwesomeIcon.Play)
            + ImGui.GetStyle().ItemSpacing.X * 2f;
        ImGui.SetNextItemWidth(-reserved);
        if (ImGui.InputTextWithHint("##soundFile", Loc.Get("Sound_File_Hint"), ref path, 260))
            settings.SoundFilePath = path;

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            fileDialog.OpenFileDialog(Loc.Get("Sound_BrowseTitle"), "Audio{.wav,.mp3,.ogg}",
                (ok, file) =>
                {
                    if (ok)
                        settings.SoundFilePath = file;
                });
        }

        SettingsUi.Tooltip(Loc.Get("Sound_Browse_Tooltip"));

        ImGui.SameLine();
        var previewClicked = ImGuiComponents.IconButton(FontAwesomeIcon.Play);

        if (showVolume)
        {
            ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
            var volumePercent = (int)Math.Round(settings.SoundVolume * 100f);
            if (ImGui.SliderInt($"{Loc.Get("Sound_Volume")}##volume", ref volumePercent, 0, 100, "%d%%"))
                settings.SoundVolume = volumePercent / 100f;
        }

        if (settings.SoundFilePath.Length == 0)
            return;

        var probe = ProbePath(settings.SoundFilePath);
        if (previewClicked)
            probe.PreviewFailed = !soundPlayer.PlayFile(settings.SoundFilePath, settings.SoundVolume);

        if (!probe.Exists)
            SettingsUi.Warning(Loc.Get("Sound_FileMissing"));
        else if (probe.PreviewFailed)
            SettingsUi.Warning(Loc.Get("Sound_PreviewFailed"));
        else if (probe.Duration is { } duration && duration > MaxAlertDuration)
            SettingsUi.Warning(string.Format(Loc.Get("Sound_FileTooLong"),
                duration.TotalSeconds, MaxAlertDuration.TotalSeconds));
    }

    private PathProbe ProbePath(string path)
    {
        if (probes.TryGetValue(path, out var probe))
            return probe;

        if (probes.Count >= MaxProbes)
            probes.Clear();

        probe = new PathProbe { Exists = File.Exists(path) };
        probe.Duration = probe.Exists ? SoundPlayer.GetDuration(path) : null;
        probes[path] = probe;
        return probe;
    }
}
