using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
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
    private string newTrigger = string.Empty;
    private readonly Dictionary<string, string> newCustomWordByCharacter = new(StringComparer.OrdinalIgnoreCase);

    public MentionsTab(MentionsConfig config)
    {
        this.config = config;
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
    }

    private void DrawSoundSettings()
    {
        var soundEnabled = config.MentionSoundEnabled;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Sound_PlayOnMatch"), ref soundEnabled))
            config.MentionSoundEnabled = soundEnabled;
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Sound_PlayOnMatch_Tooltip"));

        using var disabled = ImRaii.Disabled(!config.MentionSoundEnabled);

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

        var cooldownSeconds = config.MentionSoundCooldownMs / 1000;
        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(Loc.Get("Mentions_Sound_Cooldown"), ref cooldownSeconds, 0, 30, "%d s"))
            config.MentionSoundCooldownMs = cooldownSeconds * 1000;

        var suppressSelf = config.SuppressSoundFromSelf;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Sound_SuppressSelf"), ref suppressSelf))
            config.SuppressSoundFromSelf = suppressSelf;
    }

    private void DrawTriggers()
    {
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##newTrigger", Loc.Get("Mentions_Trigger_Hint"), ref newTrigger, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button(Loc.Get("Mentions_Trigger_Add")) || submitted) && TryAddTrigger(newTrigger))
            newTrigger = string.Empty;

        if (config.MentionTriggers.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Mentions_Trigger_Empty"));
            return;
        }

        using var table = ImRaii.Table("##triggers", 2,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##word", ImGuiTableColumnFlags.WidthStretch);

        for (var i = 0; i < config.MentionTriggers.Count; ++i)
        {
            using var id = ImRaii.PushId(i);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (SettingsUi.DangerButton(FontAwesomeIcon.Trash, Loc.Get("Mentions_Trigger_Remove_Tooltip")))
            {
                config.MentionTriggers.RemoveAt(i);
                return; // list changed; redraw next frame
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(config.MentionTriggers[i]);
        }
    }

    private bool TryAddTrigger(string input)
    {
        var trigger = input.Trim();
        if (trigger.Length == 0)
            return false;
        if (config.MentionTriggers.Any(t => t.Equals(trigger, StringComparison.OrdinalIgnoreCase)))
            return false;

        config.MentionTriggers.Add(trigger);
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
            if (!ImGui.CollapsingHeader(headerLabel))
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
            config.Characters.RemoveAt(toDelete);
    }

    /// <summary>
    /// Reads <see cref="Plugin.PlayerState"/> directly (pure Dalamud, no ClientStructs needed) so alts
    /// can be pre-configured before their first auto-learned login.
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
        if ((ImGui.Button($"{Loc.Get("Mentions_Trigger_Add")}##word") || submitted) && TryAddCustomWord(character, newWord))
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

    private static bool TryAddCustomWord(CharacterMentionSettings character, string input)
    {
        var word = input.Trim();
        if (word.Length == 0)
            return false;
        if (character.CustomWords.Any(w => w.Equals(word, StringComparison.OrdinalIgnoreCase)))
            return false;

        character.CustomWords.Add(word);
        return true;
    }
}
