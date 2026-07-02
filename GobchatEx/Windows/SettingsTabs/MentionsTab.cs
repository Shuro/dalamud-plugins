using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Chat;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Mirrors the app's Mentions page: general settings (the mention sound)
/// first, then the "Global Mentions" trigger words.
/// </summary>
internal sealed class MentionsTab : ISettingsTab
{
    public string Name => "Mentions";
    public FontAwesomeIcon Icon => FontAwesomeIcon.At;

    private readonly Configuration mutable;
    private string newTrigger = string.Empty;

    public MentionsTab(Configuration mutable)
    {
        this.mutable = mutable;
    }

    public void Draw()
    {
        SettingsUi.SectionHeader("General Settings");
        DrawSoundSettings();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader("Global Mentions",
            "Words and names that count as mentions (case-insensitive whole words).");
        DrawTriggers();
    }

    private void DrawSoundSettings()
    {
        var soundEnabled = mutable.MentionSoundEnabled;
        if (SettingsUi.Toggle("Play a sound when a mention matches", ref soundEnabled))
            mutable.MentionSoundEnabled = soundEnabled;
        ImGuiComponents.HelpMarker("Volume follows the game's sound-effects setting.");

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
        if (ImGui.SliderInt("Cooldown", ref cooldownSeconds, 0, 30, "%d s"))
            mutable.MentionSoundCooldownMs = cooldownSeconds * 1000;

        var suppressSelf = mutable.SuppressSoundFromSelf;
        if (SettingsUi.Toggle("Don't play for my own messages", ref suppressSelf))
            mutable.SuppressSoundFromSelf = suppressSelf;
    }

    private void DrawTriggers()
    {
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##newTrigger", "add a word or name…", ref newTrigger, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button("Add") || submitted) && TryAddTrigger(newTrigger))
            newTrigger = string.Empty;

        if (mutable.MentionTriggers.Count == 0)
        {
            ImGui.TextDisabled("No trigger words yet.");
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
}
