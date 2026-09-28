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
/// instant preview, and file path + browse + preview with missing/failed/too-long warnings —
/// drawing against any <see cref="IAlertSoundSettings"/>. Extracted from the Mentions tab so
/// the Groups tab's per-group sounds (Milestone 6) reuse the same widget instead of
/// duplicating it. Cooldown and volume live in the shared
/// <see cref="DrawCooldownVolumeRow"/> both tabs draw under the editor.
/// One instance serves many settings objects in the same tab:
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

    public void Draw(IAlertSoundSettings settings)
    {
        if (ImGui.RadioButton(Loc.Get("Sound_SourceGame"), !settings.SoundUseCustomFile))
            settings.SoundUseCustomFile = false;
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.Get("Sound_SourceFile"), settings.SoundUseCustomFile))
            settings.SoundUseCustomFile = true;

        if (settings.SoundUseCustomFile)
            DrawCustomFile(settings);
        else
            DrawGameEffect(settings);
    }

    /// <summary>
    /// Cooldown and volume side by side, labels above the sliders (the RangeTab style —
    /// right-hand slider labels wouldn't fit two-up in German at the minimum window width).
    /// Laid out as two shared lines (both labels, then both sliders) rather than two groups
    /// SameLine'd next to each other: items on one line always share height and baseline,
    /// while a text item following a group inherits the group's frame-padding baseline and
    /// renders a few pixels low. Cooldown comes first: volume only applies to custom sound
    /// files (game sound effects have no volume API), so game-sound mode draws the cooldown
    /// alone in the same spot. Refs are only written on an actual slider change, so callers
    /// can copy properties to locals and assign back unconditionally.
    /// </summary>
    internal static void DrawCooldownVolumeRow(string cooldownLabel, string volumeLabel,
        ref int cooldownMs, ref float volume, bool showVolume, string? cooldownTooltip = null)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var sliderWidth = MathF.Min(
            (ImGui.GetContentRegionAvail().X - spacing) / 2f,
            320f * ImGuiHelpers.GlobalScale);

        var rowStartX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted(cooldownLabel);
        if (cooldownTooltip != null)
        {
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(cooldownTooltip);
        }

        if (showVolume)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(rowStartX + sliderWidth + spacing);
            ImGui.TextUnformatted(volumeLabel);
        }

        ImGui.SetNextItemWidth(sliderWidth);
        var cooldownSeconds = cooldownMs / 1000;
        if (ImGui.SliderInt("##cooldown", ref cooldownSeconds, 0, 30, "%d s"))
            cooldownMs = cooldownSeconds * 1000;

        if (!showVolume)
            return;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(sliderWidth);
        var volumePercent = (int)Math.Round(volume * 100f);
        if (ImGui.SliderInt("##volume", ref volumePercent, 0, 100, "%d%%"))
            volume = volumePercent / 100f;
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

    private void DrawCustomFile(IAlertSoundSettings settings)
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
            fileDialog.OpenFileDialog(Loc.Get("Sound_BrowseTitle"), "Audio{.wav,.mp3,.ogg,.aif,.aiff}",
                (ok, file) =>
                {
                    if (!ok)
                        return;
                    settings.SoundFilePath = file;
                    Forget(file);
                });
        }

        SettingsUi.Tooltip(Loc.Get("Sound_Browse_Tooltip"));

        ImGui.SameLine();
        var previewClicked = ImGuiComponents.IconButton(FontAwesomeIcon.Play);

        if (settings.SoundFilePath.Length == 0)
            return;

        // A preview re-reads the file from disk (fresh probe + decode), so a file replaced under
        // the same path is picked up without a plugin reload.
        if (previewClicked)
            Forget(settings.SoundFilePath);

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

    private void Forget(string path)
    {
        probes.Remove(path);
        soundPlayer.Invalidate(path);
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
