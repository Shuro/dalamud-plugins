using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Mirrors the app's Mentions page: general settings (the mention sound)
/// first, then "Global Mentions" trigger words, then per-character "Player
/// Mentions" (Milestone 1).
/// </summary>
internal sealed class MentionsTab : IToggleableTab
{
    private static readonly FuzzyMatchLevel[] FuzzyLevels =
    [
        FuzzyMatchLevel.Conservative,
        FuzzyMatchLevel.Balanced,
        FuzzyMatchLevel.Aggressive,
    ];

    public string Name => Loc.Get("Mentions_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.At;

    public bool Enabled
    {
        get => config.MentionsEnabled;
        set => config.MentionsEnabled = value;
    }

    private readonly MentionsConfig config;
    private readonly SoundPlayer soundPlayer;
    private readonly FileDialogManager fileDialog = new();
    private string newTrigger = string.Empty;
    private readonly Dictionary<string, string> newCustomWordByCharacter = new(StringComparer.OrdinalIgnoreCase);

    // Alert sounds should stay short; anything longer gets a warning (the
    // file still plays — it's advice, not a limit).
    private static readonly TimeSpan MaxAlertDuration = TimeSpan.FromSeconds(5);

    // Per-frame File.Exists (and the duration probe) would hit the disk while
    // the tab is open, so the check runs once per distinct path (settings
    // edits and dialog picks).
    private string? checkedPath;
    private bool checkedPathExists;
    private TimeSpan? checkedPathDuration;

    // A failed preview would otherwise be invisible (the error only lands in
    // the log). Sticks until the path changes or a preview succeeds.
    private bool previewFailed;

    public MentionsTab(MentionsConfig config, SoundPlayer soundPlayer)
    {
        this.config = config;
        this.soundPlayer = soundPlayer;
    }

    public void Draw()
    {
        SettingsUi.SectionHeader(Loc.Get("Mentions_General_Header"));
        DrawSoundSettings();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Mentions_Global_Header"),
            Loc.Get("Mentions_Global_Header_Tooltip"));
        DrawTriggers();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Mentions_Player_Header"),
            Loc.Get("Mentions_Player_Header_Tooltip"));
        DrawPlayerMentions();

        fileDialog.Draw();
    }

    private void DrawSoundSettings()
    {
        var soundEnabled = config.MentionSoundEnabled;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Sound_PlayOnMatch"), ref soundEnabled))
            config.MentionSoundEnabled = soundEnabled;
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Sound_PlayOnMatch_Tooltip"));

        using var disabled = ImRaii.Disabled(!config.MentionSoundEnabled);

        if (ImGui.RadioButton(Loc.Get("Mentions_Sound_SourceGame"), !config.MentionSoundUseCustomFile))
            config.MentionSoundUseCustomFile = false;
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.Get("Mentions_Sound_SourceFile"), config.MentionSoundUseCustomFile))
            config.MentionSoundUseCustomFile = true;

        if (config.MentionSoundUseCustomFile)
            DrawCustomFileSettings();
        else
            DrawGameSoundSettings();

        DrawCooldownVolumeRow(showVolume: config.MentionSoundUseCustomFile);

        var suppressSelf = config.SuppressSoundFromSelf;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Sound_SuppressSelf"), ref suppressSelf))
            config.SuppressSoundFromSelf = suppressSelf;
    }

    /// <summary>
    /// Cooldown and volume side by side, labels above the sliders (the RangeTab style —
    /// right-hand slider labels wouldn't fit two-up in German at the minimum window width).
    /// Laid out as two shared lines (both labels, then both sliders) rather than two groups
    /// SameLine'd next to each other: items on one line always share height and baseline,
    /// while a text item following a group inherits the group's frame-padding baseline and
    /// renders a few pixels low. Cooldown comes first: volume only applies to custom sound
    /// files (game sound effects have no volume API), so game-sound mode draws the cooldown
    /// alone in the same spot.
    /// </summary>
    private void DrawCooldownVolumeRow(bool showVolume)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var sliderWidth = MathF.Min(
            (ImGui.GetContentRegionAvail().X - spacing) / 2f,
            320f * ImGuiHelpers.GlobalScale);

        var rowStartX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted(Loc.Get("Mentions_Sound_Cooldown"));
        if (showVolume)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(rowStartX + sliderWidth + spacing);
            ImGui.TextUnformatted(Loc.Get("Mentions_Sound_Volume"));
        }

        ImGui.SetNextItemWidth(sliderWidth);
        var cooldownSeconds = config.MentionSoundCooldownMs / 1000;
        if (ImGui.SliderInt("##cooldown", ref cooldownSeconds, 0, 30, "%d s"))
            config.MentionSoundCooldownMs = cooldownSeconds * 1000;

        if (!showVolume)
            return;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(sliderWidth);
        var volumePercent = (int)Math.Round(config.MentionSoundVolume * 100f);
        if (ImGui.SliderInt("##volume", ref volumePercent, 0, 100, "%d%%"))
            config.MentionSoundVolume = volumePercent / 100f;
    }

    private void DrawGameSoundSettings()
    {
        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("##soundEffect", GameSound.Name(config.MentionSoundEffect)))
        {
            if (combo)
            {
                for (var effect = GameSound.Min; effect <= GameSound.Max; ++effect)
                {
                    if (!ImGui.Selectable(GameSound.Name(effect), effect == config.MentionSoundEffect))
                        continue;

                    config.MentionSoundEffect = effect;
                    SoundPlayer.Play(effect); // instant preview of the choice
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
            SoundPlayer.Play(config.MentionSoundEffect);
    }

    private void DrawCustomFileSettings()
    {
        var path = config.MentionSoundFilePath;
        var reserved = SettingsUi.IconButtonWidth(FontAwesomeIcon.FolderOpen)
            + SettingsUi.IconButtonWidth(FontAwesomeIcon.Play)
            + ImGui.GetStyle().ItemSpacing.X * 2f;
        ImGui.SetNextItemWidth(-reserved);
        if (ImGui.InputTextWithHint("##soundFile", Loc.Get("Mentions_Sound_File_Hint"), ref path, 260))
            config.MentionSoundFilePath = path;

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            fileDialog.OpenFileDialog(Loc.Get("Mentions_Sound_BrowseTitle"), "Audio{.wav,.mp3,.ogg}",
                (ok, file) =>
                {
                    if (ok)
                        config.MentionSoundFilePath = file;
                });
        }

        SettingsUi.Tooltip(Loc.Get("Mentions_Sound_Browse_Tooltip"));

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
            previewFailed = !soundPlayer.PlayFile(config.MentionSoundFilePath, config.MentionSoundVolume);

        if (config.MentionSoundFilePath.Length == 0)
            return;

        CheckPathCached(config.MentionSoundFilePath);
        if (!checkedPathExists)
            SettingsUi.Warning(Loc.Get("Mentions_Sound_FileMissing"));
        else if (previewFailed)
            SettingsUi.Warning(Loc.Get("Mentions_Sound_PreviewFailed"));
        else if (checkedPathDuration is { } duration && duration > MaxAlertDuration)
            SettingsUi.Warning(string.Format(Loc.Get("Mentions_Sound_FileTooLong"),
                duration.TotalSeconds, MaxAlertDuration.TotalSeconds));
    }

    private void CheckPathCached(string path)
    {
        if (checkedPath == path)
            return;

        checkedPath = path;
        checkedPathExists = File.Exists(path);
        checkedPathDuration = checkedPathExists ? SoundPlayer.GetDuration(path) : null;
        previewFailed = false;
    }

    private void DrawTriggers()
    {
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##newTrigger", Loc.Get("Mentions_Trigger_Hint"), ref newTrigger, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button(Loc.Get("Mentions_Trigger_Add")) || submitted) && TryAddUnique(config.MentionTriggers, newTrigger))
            newTrigger = string.Empty;

        if (config.MentionTriggers.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Mentions_Trigger_Empty"));
            return;
        }

        var removed = SettingsUi.RemovableListColumns("##triggers", config.MentionTriggers.Count,
            i => config.MentionTriggers[i], Loc.Get("Mentions_Trigger_Remove_Tooltip"),
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter);
        if (removed >= 0)
            config.MentionTriggers.RemoveAt(removed);
    }

    /// <summary>Trims, rejects empty input and case-insensitive duplicates, then appends. True when added.</summary>
    private static bool TryAddUnique(List<string> list, string input)
    {
        var value = input.Trim();
        if (value.Length == 0)
            return false;
        if (list.Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)))
            return false;

        list.Add(value);
        return true;
    }

    private void DrawPlayerMentions()
    {
        var enabled = config.PlayerMentionsEnabled;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Player_MatchLoggedIn"), ref enabled))
            config.PlayerMentionsEnabled = enabled;

        using var disabledSection = ImRaii.Disabled(!config.PlayerMentionsEnabled);

        DrawAddCurrentCharacterButton();
        ImGuiHelpers.ScaledDummy(4f);

        if (config.Characters.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Mentions_Player_Empty"));
            return;
        }

        var toDelete = -1;
        for (var i = 0; i < config.Characters.Count; ++i)
        {
            var character = config.Characters[i];
            using var id = ImRaii.PushId(i);

            var headerLabel = character.Active
                ? character.Name
                : string.Format(Loc.Get("Mentions_Character_Inactive"), character.Name);
            // ### keeps the header's ID stable (unique via the PushId above) so toggling
            // Active — which reformats the label — doesn't collapse an open header.
            if (!ImGui.CollapsingHeader($"{headerLabel}###character-header"))
                continue;

            using var indent = ImRaii.PushIndent();

            var active = character.Active;
            if (ImGui.Checkbox(Loc.Get("Mentions_Character_Active"), ref active))
                character.Active = active;

            ImGui.SameLine();
            var removeClicked = SettingsUi.DangerButton(FontAwesomeIcon.Trash,
                Loc.Get("Mentions_Character_Remove"), Loc.Get("Mentions_Character_Remove_Tooltip"));

            if (removeClicked)
            {
                toDelete = i;
                continue;
            }

            DrawCharacterOptions(character);
            ImGuiHelpers.ScaledDummy(6f);
        }

        if (toDelete >= 0)
        {
            // Drop any half-typed custom word for the deleted character, or it would silently
            // resurface in the input box if a character with the same name is re-added.
            newCustomWordByCharacter.Remove(config.Characters[toDelete].Name);
            config.Characters.RemoveAt(toDelete);
        }
    }

    /// <summary>
    /// Reads <see cref="Plugin.PlayerState"/> directly (pure Dalamud, no ClientStructs needed).
    /// This button is the only way characters get remembered — the app's login auto-learn was
    /// not ported.
    /// </summary>
    private void DrawAddCurrentCharacterButton()
    {
        var loaded = Plugin.PlayerState.IsLoaded;
        using (ImRaii.Disabled(!loaded))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserPlus, Loc.Get("Mentions_Character_AddCurrent")))
                AddCurrentCharacter();
        }

        if (!loaded)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.Get("Mentions_Character_LogInFirst"));
        }
    }

    private void AddCurrentCharacter()
    {
        var name = Plugin.PlayerState.CharacterName.Trim();
        if (name.Length == 0)
            return;

        var existing = config.Characters
            .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Active = true; // explicit user action: turn it on even if previously off
            return;
        }

        config.Characters.Add(new CharacterMentionSettings { Name = name, Active = true });
    }

    private void DrawCharacterOptions(CharacterMentionSettings character)
    {
        ImGui.TextUnformatted(Loc.Get("Mentions_Character_MatchLabel"));
        ImGui.SameLine();
        DrawNamePartCheckbox(Loc.Get("Mentions_Character_FullName"), () => character.MatchFullName, v => character.MatchFullName = v);
        ImGui.SameLine();
        DrawNamePartCheckbox(Loc.Get("Mentions_Character_FirstName"), () => character.MatchFirstName, v => character.MatchFirstName = v);
        ImGui.SameLine();
        DrawNamePartCheckbox(Loc.Get("Mentions_Character_LastName"), () => character.MatchLastName, v => character.MatchLastName = v);

        DrawNamePartCheckbox(Loc.Get("Mentions_Character_FirstNamePartial"),
            () => character.MatchFirstNamePartial, v => character.MatchFirstNamePartial = v);
        ImGui.SameLine();
        DrawNamePartCheckbox(Loc.Get("Mentions_Character_LastNamePartial"),
            () => character.MatchLastNamePartial, v => character.MatchLastNamePartial = v);

        DrawNamePartCheckbox(Loc.Get("Mentions_Character_Miqote"),
            () => character.MatchMiqote, v => character.MatchMiqote = v);

        var fuzzy = character.MatchFuzzy;
        if (ImGui.Checkbox(Loc.Get("Mentions_Character_Fuzzy"), ref fuzzy))
            character.MatchFuzzy = fuzzy;
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Character_Fuzzy_Tooltip"));

        using (ImRaii.Disabled(!character.MatchFuzzy))
        {
            ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
            using var combo = ImRaii.Combo("##fuzzyLevel", character.FuzzyLevel.ToString());
            if (combo)
            {
                foreach (var level in FuzzyLevels)
                {
                    if (ImGui.Selectable(level.ToString(), level == character.FuzzyLevel))
                        character.FuzzyLevel = level;
                }
            }
        }

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.TextUnformatted(Loc.Get("Mentions_Character_CustomWords"));
        DrawCustomWords(character);
    }

    private static void DrawNamePartCheckbox(string label, Func<bool> get, Action<bool> set)
    {
        var value = get();
        if (ImGui.Checkbox(label, ref value))
            set(value);
    }

    private void DrawCustomWords(CharacterMentionSettings character)
    {
        if (!newCustomWordByCharacter.TryGetValue(character.Name, out var newWord))
            newWord = string.Empty;

        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##newWord", Loc.Get("Mentions_Character_CustomWord_Hint"), ref newWord, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        newCustomWordByCharacter[character.Name] = newWord;

        ImGui.SameLine();
        if ((ImGui.Button($"{Loc.Get("Mentions_Trigger_Add")}##word") || submitted) && TryAddUnique(character.CustomWords, newWord))
            newCustomWordByCharacter[character.Name] = string.Empty;

        for (var i = 0; i < character.CustomWords.Count; ++i)
        {
            using var id = ImRaii.PushId(i);
            if (SettingsUi.DangerButton(FontAwesomeIcon.Trash, Loc.Get("Mentions_CustomWord_Remove_Tooltip")))
            {
                character.CustomWords.RemoveAt(i);
                break;
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(character.CustomWords[i]);
        }
    }

}
