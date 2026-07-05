using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;
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
        get => mutable.MentionsEnabled;
        set => mutable.MentionsEnabled = value;
    }

    private readonly Configuration mutable;
    private string newTrigger = string.Empty;
    private readonly Dictionary<string, string> newCustomWordByCharacter = new(StringComparer.OrdinalIgnoreCase);

    public MentionsTab(Configuration mutable)
    {
        this.mutable = mutable;
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
        var soundEnabled = mutable.MentionSoundEnabled;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Sound_PlayOnMatch"), ref soundEnabled))
            mutable.MentionSoundEnabled = soundEnabled;
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Sound_PlayOnMatch_Tooltip"));

        using var disabled = ImRaii.Disabled(!mutable.MentionSoundEnabled);

        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("##soundEffect", GameSound.Name(mutable.MentionSoundEffect)))
        {
            if (combo)
            {
                for (var effect = GameSound.Min; effect <= GameSound.Max; ++effect)
                {
                    if (!ImGui.Selectable(GameSound.Name(effect), effect == mutable.MentionSoundEffect))
                        continue;

                    mutable.MentionSoundEffect = effect;
                    SoundPlayer.Play(effect); // instant preview of the (staged) choice
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Play))
            SoundPlayer.Play(mutable.MentionSoundEffect);

        var cooldownSeconds = mutable.MentionSoundCooldownMs / 1000;
        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(Loc.Get("Mentions_Sound_Cooldown"), ref cooldownSeconds, 0, 30, "%d s"))
            mutable.MentionSoundCooldownMs = cooldownSeconds * 1000;

        var suppressSelf = mutable.SuppressSoundFromSelf;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Sound_SuppressSelf"), ref suppressSelf))
            mutable.SuppressSoundFromSelf = suppressSelf;
    }

    private void DrawTriggers()
    {
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##newTrigger", Loc.Get("Mentions_Trigger_Hint"), ref newTrigger, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button(Loc.Get("Mentions_Trigger_Add")) || submitted) && TryAddTrigger(newTrigger))
            newTrigger = string.Empty;

        if (mutable.MentionTriggers.Count == 0)
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

        for (var i = 0; i < mutable.MentionTriggers.Count; ++i)
        {
            using var id = ImRaii.PushId(i);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                mutable.MentionTriggers.RemoveAt(i);
                return; // list changed; redraw next frame
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(mutable.MentionTriggers[i]);
        }
    }

    private bool TryAddTrigger(string input)
    {
        var trigger = input.Trim();
        if (trigger.Length == 0)
            return false;
        if (mutable.MentionTriggers.Any(t => t.Equals(trigger, StringComparison.OrdinalIgnoreCase)))
            return false;

        mutable.MentionTriggers.Add(trigger);
        return true;
    }

    private void DrawPlayerMentions()
    {
        var enabled = mutable.PlayerMentionsEnabled;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Player_MatchLoggedIn"), ref enabled))
            mutable.PlayerMentionsEnabled = enabled;

        using var disabledSection = ImRaii.Disabled(!mutable.PlayerMentionsEnabled);

        DrawAddCurrentCharacterButton();
        ImGuiHelpers.ScaledDummy(4f);

        if (mutable.Characters.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Mentions_Player_Empty"));
            return;
        }

        var toDelete = -1;
        for (var i = 0; i < mutable.Characters.Count; ++i)
        {
            var character = mutable.Characters[i];
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
            var canRemove = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
            bool removeClicked;
            using (ImRaii.Disabled(!canRemove))
                removeClicked = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, Loc.Get("Mentions_Character_Remove"));

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted(Loc.Get("Mentions_Character_Remove_Tooltip"));
            }

            if (removeClicked)
            {
                toDelete = i;
                continue;
            }

            DrawCharacterOptions(character);
            ImGuiHelpers.ScaledDummy(6f);
        }

        if (toDelete >= 0)
            mutable.Characters.RemoveAt(toDelete);
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

        var existing = mutable.Characters
            .FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Active = true; // explicit user action: turn it on even if previously off
            return;
        }

        mutable.Characters.Add(new CharacterMentionSettings { Name = name, Active = true });
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
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
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
