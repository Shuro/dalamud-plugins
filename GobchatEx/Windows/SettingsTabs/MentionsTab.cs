using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
    private readonly FormattingConfig formatting;
    private readonly Action openHistory;
    private readonly FileDialogManager fileDialog = new();
    private readonly AlertSoundEditor soundEditor;
    private string newTrigger = string.Empty;
    private string testMessage = string.Empty;
    private readonly Dictionary<string, string> newCustomWordByCharacter = new(StringComparer.OrdinalIgnoreCase);

    public MentionsTab(MentionsConfig config, SoundPlayer soundPlayer, FormattingConfig formatting,
        Action openHistory)
    {
        this.config = config;
        this.formatting = formatting;
        this.openHistory = openHistory;
        soundEditor = new AlertSoundEditor(fileDialog, soundPlayer);
    }

    public void Draw()
    {
        // Same bell as the Quickbar — the history window is where mentions land, so it gets
        // a door from the tab that configures them.
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Bell, Loc.Get("MentionHistory_Title")))
            openHistory();
        ImGuiHelpers.ScaledDummy(6f);

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

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Mentions_Test_Header"),
            Loc.Get("Mentions_Test_Header_Tooltip"));
        DrawTester();

        fileDialog.Draw();
    }

    private void DrawTester()
    {
        // The matcher is rebuilt every frame while there is test text: the preview must
        // reflect edits made in this very tab (triggers, characters, fuzzy level) instantly,
        // and building it for one short string is far cheaper than tracking every config
        // change. No token rules — this tests the mention overlay in isolation, exactly like
        // the Chat 2 provider's mention-bypass segmenter.
        IReadOnlyList<SegmentSpan> spans = [];
        var hasMatch = false;
        MentionRules? rules = null;
        if (testMessage.Length > 0)
        {
            rules = ChatListener.BuildMentionRules(config);
            var segmenter = new MessageSegmenter((IReadOnlyList<TokenRule>)[], rules);
            if (segmenter.Segment([testMessage]) is { HasMention: true } result)
            {
                spans = result.RunSpans[0].Where(s => s.Type == SegmentType.Mention).ToArray();
                hasMatch = true;
            }
        }

        // The preview sits above the input in an input-styled frame (ChildFrame = FrameBg
        // background + frame padding), so it reads as the output field of the input below —
        // loose colored text floating over the input looked like debris. Always drawn, one
        // line tall when empty, growing with the wrapped line count for long messages.
        var style = ImGui.GetStyle();
        var boxWidth = ImGui.GetContentRegionAvail().X;
        var wrapWidth = boxWidth - style.FramePadding.X * 2f;
        var lines = testMessage.Length > 0
            ? SettingsUi.HighlightedTextLineCount(testMessage, spans, wrapWidth)
            : 1;
        var boxSize = new Vector2(boxWidth, lines * ImGui.GetTextLineHeight() + style.FramePadding.Y * 2f);

        using (var frame = ImRaii.ChildFrame(ImGui.GetID("##mentionTestPreview"), boxSize))
        {
            if (frame)
            {
                if (testMessage.Length > 0)
                {
                    // The message always shows, even without a match — an all-dim line is the
                    // "nothing matched" signal, and the text stays visible for tweaking (a
                    // note in its place hid the very message being tested).
                    using var dim = ImRaii.PushColor(ImGuiCol.Text,
                        ImGui.GetColorU32(ImGuiCol.TextDisabled), !hasMatch);
                    var mentionStyles = rules?.Styles ?? [];
                    SettingsUi.HighlightedTextWrapped(testMessage, spans,
                        (span, _) => RgbaColor.ToVector4(MentionStyleResolver.Resolve(
                            span.StyleId, mentionStyles, formatting.MentionStyle.Foreground, 0).Foreground),
                        wrapWidth);
                }
            }
        }

        ImGui.SetNextItemWidth(boxWidth);
        ImGui.InputTextWithHint("##mentionTest", Loc.Get("Mentions_Test_Hint"), ref testMessage, 256);
    }

    private void DrawSoundSettings()
    {
        // Highlight suppression is independent of the sound alert, so it stays outside the
        // sound-disabled scope below.
        var suppressHighlight = config.SuppressHighlightFromSelf;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Highlight_SuppressSelf"), ref suppressHighlight))
            config.SuppressHighlightFromSelf = suppressHighlight;
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Highlight_SuppressSelf_Tooltip"));

        var soundEnabled = config.MentionSoundEnabled;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Sound_PlayOnMatch"), ref soundEnabled))
            config.MentionSoundEnabled = soundEnabled;
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Sound_PlayOnMatch_Tooltip"));

        using var disabled = ImRaii.Disabled(!config.MentionSoundEnabled);

        // The volume slider stays in this tab's two-up cooldown/volume row below rather than
        // inside the shared editor (showVolume: false).
        soundEditor.Draw(config, showVolume: false);

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

        var removed = DrawStyledWordTable("##triggers", config.MentionTriggers, Loc.Get("Mentions_Trigger_Remove_Tooltip"));
        if (removed >= 0)
            config.MentionTriggers.RemoveAt(removed);
    }

    /// <summary>Trims, rejects empty input and case-insensitive duplicates, then appends
    /// (with no color override — set from the swatches afterward). True when added.</summary>
    private static bool TryAddUnique(List<MentionTrigger> list, string input)
    {
        var value = input.Trim();
        if (value.Length == 0)
            return false;
        if (list.Any(x => x.Word.Equals(value, StringComparison.OrdinalIgnoreCase)))
            return false;

        list.Add(new MentionTrigger { Word = value });
        return true;
    }

    /// <summary>
    /// Trash | word | foreground swatch | glow swatch, one row per entry — the shared per-word
    /// color/glow override table for both global triggers and per-character custom words. Returns
    /// the index whose trash button was clicked this frame, or -1; the caller removes after the
    /// loop so the list isn't mutated mid-draw.
    /// </summary>
    private static int DrawStyledWordTable(string id, List<MentionTrigger> words, string removeTooltip)
    {
        var removed = -1;
        using var table = ImRaii.Table(id, 4,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter);
        if (!table)
            return removed;

        ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Mentions_Column_Word"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Loc.Get("Mentions_Column_Color"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Mentions_Column_Glow"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        for (var i = 0; i < words.Count; ++i)
        {
            using var rowId = ImRaii.PushId(i);
            var word = words[i];

            ImGui.TableNextColumn();
            if (SettingsUi.DangerButton(FontAwesomeIcon.Trash, removeTooltip))
                removed = i;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(word.Word);

            ImGui.TableNextColumn();
            var foreground = word.Foreground;
            if (SettingsUi.RgbaColorEdit("##fg", ref foreground, allowAlpha: false))
                word.Foreground = foreground;

            ImGui.TableNextColumn();
            var glow = word.Glow;
            if (SettingsUi.RgbaColorEdit("##glow", ref glow, allowAlpha: false))
                word.Glow = glow;
        }

        return removed;
    }

    private void DrawPlayerMentions()
    {
        var enabled = config.PlayerMentionsEnabled;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Player_MatchLoggedIn"), ref enabled))
            config.PlayerMentionsEnabled = enabled;

        using var disabledSection = ImRaii.Disabled(!config.PlayerMentionsEnabled);

        DrawAddCurrentCharacterButton();
        DrawCurrentCharacterInactiveWarning();
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
            if (SettingsUi.Toggle(Loc.Get("Mentions_Character_Active"), ref active))
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

    /// <summary>
    /// The silent no-match gap behind "my name doesn't trigger anything" reports: player
    /// mentions are on, but the logged-in character was never added (registration is
    /// manual-only) or its entry is inactive — then neither highlight nor sound can fire.
    /// Same name comparison as ChatListener.BuildMentionRules.
    /// </summary>
    private void DrawCurrentCharacterInactiveWarning()
    {
        if (!config.PlayerMentionsEnabled || !Plugin.PlayerState.IsLoaded)
            return;

        var name = Plugin.PlayerState.CharacterName;
        if (name.Length == 0)
            return;

        if (config.Characters.Any(c => c.Active && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        SettingsUi.Warning(string.Format(Loc.Get("Mentions_Player_CurrentNotActive"), name));
    }

    private void DrawCharacterOptions(CharacterMentionSettings character)
    {
        ImGui.TextUnformatted(Loc.Get("Mentions_Character_MatchLabel"));

        // A 2-column borderless grid pairing each name part with its partial/special variant
        // (Miqo'te is the full name's extra, the partials belong to first/last), so related
        // switches read side by side. A partial's switch is disabled while its base name part
        // is off — PlayerMentionResolver mirrors that dependency, so a greyed-out switch can't
        // still match. Miqo'te is disabled when the name has no apostrophe: the resolver would
        // derive nothing from it anyway, so an enabled switch would be a no-op lie.
        using (var table = ImRaii.Table("##nameMatches", 2))
        {
            if (table)
            {
                var fullName = character.MatchFullName;
                if (ToggleCell(Loc.Get("Mentions_Character_FullName"), ref fullName))
                    character.MatchFullName = fullName;

                var miqote = character.MatchMiqote;
                if (ToggleCell(Loc.Get("Mentions_Character_Miqote"), ref miqote,
                        Loc.Get("Mentions_Character_Miqote_Tooltip"), disabled: !character.Name.Contains('\'')))
                    character.MatchMiqote = miqote;

                var firstName = character.MatchFirstName;
                if (ToggleCell(Loc.Get("Mentions_Character_FirstName"), ref firstName))
                    character.MatchFirstName = firstName;

                var firstPartial = character.MatchFirstNamePartial;
                if (ToggleCell(Loc.Get("Mentions_Character_FirstNamePartial"), ref firstPartial,
                        Loc.Get("Mentions_Character_Partial_Tooltip"), disabled: !character.MatchFirstName))
                    character.MatchFirstNamePartial = firstPartial;

                var lastName = character.MatchLastName;
                if (ToggleCell(Loc.Get("Mentions_Character_LastName"), ref lastName))
                    character.MatchLastName = lastName;

                var lastPartial = character.MatchLastNamePartial;
                if (ToggleCell(Loc.Get("Mentions_Character_LastNamePartial"), ref lastPartial,
                        Loc.Get("Mentions_Character_Partial_Tooltip"), disabled: !character.MatchLastName))
                    character.MatchLastNamePartial = lastPartial;

                DrawFuzzyCells(character);
            }
        }

        ImGuiHelpers.ScaledDummy(6f);
        ImGui.TextUnformatted(Loc.Get("Mentions_Character_NameStyle"));
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Character_NameStyle_Tooltip"));
        ImGui.SameLine();
        var nameForeground = character.NameForeground;
        if (SettingsUi.RgbaColorEdit("##nameFg", ref nameForeground, allowAlpha: false))
            character.NameForeground = nameForeground;
        ImGui.SameLine();
        var nameGlow = character.NameGlow;
        if (SettingsUi.RgbaColorEdit("##nameGlow", ref nameGlow, allowAlpha: false))
            character.NameGlow = nameGlow;

        ImGuiHelpers.ScaledDummy(4f);
        ImGui.TextUnformatted(Loc.Get("Mentions_Character_CustomWords"));
        DrawCustomWords(character);
    }

    /// <summary>One cell of the name-match grid: a toggle switch, optionally disabled with a
    /// hover tooltip on its label (shown while disabled too, explaining the dependency).
    /// Returns true when the value changed.</summary>
    private static bool ToggleCell(string label, ref bool value, string? tooltip = null, bool disabled = false)
    {
        ImGui.TableNextColumn();
        bool changed;
        using (ImRaii.Disabled(disabled))
            changed = SettingsUi.Toggle(label, ref value);
        if (tooltip != null)
            SettingsUi.Tooltip(tooltip);
        return changed;
    }

    /// <summary>The fuzzy toggle (with its help marker) and the sensitivity combo as the
    /// name-match grid's last row.</summary>
    private void DrawFuzzyCells(CharacterMentionSettings character)
    {
        ImGui.TableNextColumn();
        var fuzzy = character.MatchFuzzy;
        if (SettingsUi.Toggle(Loc.Get("Mentions_Character_Fuzzy"), ref fuzzy))
            character.MatchFuzzy = fuzzy;
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.Get("Mentions_Character_Fuzzy_Tooltip"));

        ImGui.TableNextColumn();
        using var disabled = ImRaii.Disabled(!character.MatchFuzzy);
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        using var combo = ImRaii.Combo("##fuzzyLevel", character.FuzzyLevel.ToString());
        if (!combo)
            return;

        foreach (var level in FuzzyLevels)
        {
            if (ImGui.Selectable(level.ToString(), level == character.FuzzyLevel))
                character.FuzzyLevel = level;
        }
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

        if (character.CustomWords.Count == 0)
            return;

        var removed = DrawStyledWordTable("##customWords", character.CustomWords, Loc.Get("Mentions_CustomWord_Remove_Tooltip"));
        if (removed >= 0)
            character.CustomWords.RemoveAt(removed);
    }

}
