using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Milestone 2: custom player groups (reorderable, user-named, per-group player list) plus the game's
/// seven fixed friend-list display groups (active/color only, no add/remove/rename). Mirrors
/// MentionsTab's per-item CollapsingHeader + nested flat-list-editor pattern for custom groups.
/// </summary>
internal sealed class GroupsTab : IToggleableTab
{
    public string Name => Loc.Get("Groups_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Users;

    public bool Enabled
    {
        get => config.GroupsEnabled;
        set => config.GroupsEnabled = value;
    }

    private readonly GroupsConfig config;
    private readonly ChatTwoStyleProvider chatTwoStyles;
    private readonly FileDialogManager fileDialog = new();
    private readonly AlertSoundEditor soundEditor;
    private string newGroupName = string.Empty;

    // Scratch text for the rename popup; shared across groups is fine — only
    // one popup can be open, and it's re-seeded from the group on open.
    private string renameBuffer = string.Empty;

    public GroupsTab(GroupsConfig config, ChatTwoStyleProvider chatTwoStyles, SoundPlayer soundPlayer)
    {
        this.config = config;
        this.chatTwoStyles = chatTwoStyles;
        soundEditor = new AlertSoundEditor(fileDialog, soundPlayer);
    }

    public void Draw()
    {
        SettingsUi.SectionHeader(Loc.Get("Groups_Custom_Header"), Loc.Get("Groups_Custom_Header_Tooltip"));
        DrawCustomGroups();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Groups_Friend_Header"), Loc.Get("Groups_Friend_Header_Tooltip"));
        DrawFriendGroups();

        fileDialog.Draw();
    }

    private void DrawCustomGroups()
    {
        DrawAddGroupControl();
        ImGuiHelpers.ScaledDummy(4f);

        if (config.Groups.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Groups_Custom_Empty"));
            return;
        }

        var toDelete = -1;
        for (var i = 0; i < config.Groups.Count; ++i)
        {
            var group = config.Groups[i];
            using var id = ImRaii.PushId(group.Id);

            var headerLabel = group.Active
                ? group.Name
                : string.Format(Loc.Get("Groups_Custom_Inactive"), group.Name);
            // ### keeps the header's ID stable (unique via the PushId above) so renames and
            // Active toggles — both reformat the label — don't collapse an open header.
            if (!ImGui.CollapsingHeader($"{headerLabel}###group-header"))
                continue;

            using var indent = ImRaii.PushIndent();

            var active = group.Active;
            if (ImGui.Checkbox(Loc.Get("Groups_Custom_Active"), ref active))
                group.Active = active;

            ImGui.SameLine();
            DrawRenameControl(group);

            ImGui.SameLine();
            var removeClicked = SettingsUi.DangerButton(FontAwesomeIcon.Trash,
                Loc.Get("Groups_Custom_Remove"), Loc.Get("Groups_Custom_Remove_Tooltip"));

            if (removeClicked)
            {
                toDelete = i;
                continue;
            }

            ImGuiHelpers.ScaledDummy(4f);
            DrawGroupColors(group);

            ImGuiHelpers.ScaledDummy(4f);
            DrawGroupSound(group);

            ImGuiHelpers.ScaledDummy(4f);
            ImGui.TextUnformatted(Loc.Get("Groups_Custom_Players"));
            DrawPlayers(group);
            ImGuiHelpers.ScaledDummy(6f);
        }

        if (toDelete >= 0)
            config.Groups.RemoveAt(toDelete);

        ImGuiHelpers.ScaledDummy(4f);
        DrawSoundCooldown();
    }

    /// <summary>
    /// The group's alert sound (Milestone 6): an enable toggle, then the shared sound editor
    /// while enabled (hidden when off to keep each group's header compact — unlike the Mentions
    /// tab there can be many of these). The editor call is safe to repeat per group because the
    /// loop already scopes each group with PushId.
    /// </summary>
    private void DrawGroupSound(PlayerGroup group)
    {
        var soundEnabled = group.SoundEnabled;
        if (SettingsUi.Toggle(Loc.Get("Groups_Sound_PlayOnMessage"), ref soundEnabled))
            group.SoundEnabled = soundEnabled;
        ImGuiComponents.HelpMarker(Loc.Get("Groups_Sound_PlayOnMessage_Tooltip"));

        if (!group.SoundEnabled)
            return;

        using var indent = ImRaii.PushIndent();
        soundEditor.Draw(group, showVolume: true);
    }

    /// <summary>
    /// The cooldown all group sounds share (ADR 0005). Always drawn (stable layout), disabled
    /// until any group's sound is enabled.
    /// </summary>
    private void DrawSoundCooldown()
    {
        var anySound = config.Groups.Any(g => g.SoundEnabled);
        using (ImRaii.Disabled(!anySound))
        {
            ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
            var cooldownSeconds = config.GroupSoundCooldownMs / 1000;
            if (ImGui.SliderInt($"{Loc.Get("Groups_Sound_Cooldown")}##groupSoundCooldown",
                    ref cooldownSeconds, 0, 30, "%d s"))
                config.GroupSoundCooldownMs = cooldownSeconds * 1000;
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(Loc.Get("Groups_Sound_Cooldown_Tooltip"));
    }

    private void DrawAddGroupControl()
    {
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##newGroupName", Loc.Get("Groups_Custom_NameHint"), ref newGroupName, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button(Loc.Get("Groups_Custom_Add")) || submitted) && TryAddGroup(newGroupName))
            newGroupName = string.Empty;

        // A purely numeric name would collide with the "<idx> add|remove|clear ..." command grammar
        // (Task 6), which treats a numeric locator as a 1-based index rather than a name.
        if (IsPureNumericName(newGroupName.Trim()))
            ImGui.TextColored(ImGuiColors.DalamudRed, Loc.Get("Groups_Custom_NumericName_Error"));
    }

    private bool TryAddGroup(string input)
    {
        var name = input.Trim();
        if (name.Length == 0 || IsPureNumericName(name))
            return false;
        if (config.Groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return false;

        config.Groups.Add(new PlayerGroup { Id = Guid.NewGuid().ToString(), Name = name, Active = true });
        return true;
    }

    private static bool IsPureNumericName(string name) => name.Length > 0 && int.TryParse(name, out _);

    /// <summary>
    /// Pencil button opening a rename popup. Renaming is safe: everything downstream
    /// (group rules, Chat 2 styling) is keyed by the group's Id, and the /gobchat group
    /// command resolves names live per invocation — only saved user macros using the old
    /// name stop matching. Validation mirrors <see cref="TryAddGroup"/>, except the
    /// duplicate check skips the group itself so case-only renames pass.
    /// </summary>
    private void DrawRenameControl(PlayerGroup group)
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Pen, Loc.Get("Groups_Custom_Rename")))
        {
            renameBuffer = group.Name;
            ImGui.OpenPopup("##rename-popup");
        }

        SettingsUi.Tooltip(Loc.Get("Groups_Custom_Rename_Tooltip"));

        using var popup = ImRaii.Popup("##rename-popup");
        if (!popup)
            return;

        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##rename", Loc.Get("Groups_Custom_NameHint"),
            ref renameBuffer, 64, ImGuiInputTextFlags.EnterReturnsTrue);

        var trimmed = renameBuffer.Trim();
        var numeric = IsPureNumericName(trimmed);
        var duplicate = config.Groups.Any(g => !ReferenceEquals(g, group)
            && g.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        var valid = trimmed.Length > 0 && !numeric && !duplicate;

        if (numeric)
            ImGui.TextColored(ImGuiColors.DalamudRed, Loc.Get("Groups_Custom_NumericName_Error"));
        else if (duplicate)
            ImGui.TextColored(ImGuiColors.DalamudRed, Loc.Get("Groups_Custom_DuplicateName_Error"));

        bool clicked;
        using (ImRaii.Disabled(!valid))
            clicked = ImGui.Button(Loc.Get("Groups_Custom_Rename"));

        if ((clicked || submitted) && valid)
        {
            group.Name = trimmed;
            ImGui.CloseCurrentPopup();
        }
    }

    /// <summary>
    /// The group's three color swatches under the same column titles the Friend Groups
    /// table uses, so it's clear which swatch drives what.
    /// </summary>
    private void DrawGroupColors(PlayerGroup group)
    {
        using var table = ImRaii.Table("##group-colors", 3, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(Loc.Get("Groups_Column_NameColor"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Groups_Column_NameGlow"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Groups_Column_ChatTwoBackground"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        var foreground = group.Foreground;
        if (SettingsUi.RgbaColorEdit("##fg", ref foreground, allowAlpha: false))
            group.Foreground = foreground;

        ImGui.TableNextColumn();
        var glow = group.Glow;
        if (SettingsUi.RgbaColorEdit("##glow", ref glow, allowAlpha: false))
            group.Glow = glow;

        ImGui.TableNextColumn();
        DrawChatTwoBackgroundEdit(group);
    }

    private void DrawPlayers(PlayerGroup group)
    {
        var hasTarget = TryGetTargetedPlayer(out var targetName, out var targetWorld);

        bool addClicked;
        using (ImRaii.Disabled(!hasTarget))
            addClicked = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Crosshairs, Loc.Get("Groups_Custom_AddTarget"));

        SettingsUi.Tooltip(hasTarget
            ? string.Format(Loc.Get("Groups_Custom_AddTarget_Tooltip"), GroupMembershipActions.FormatPlayer(targetName, targetWorld))
            : Loc.Get("Groups_Custom_NoPlayerTarget"));

        if (addClicked && hasTarget)
            TryAddPlayer(group, targetName, targetWorld);

        var removedMember = SettingsUi.RemovableListColumns("##members", group.Members.Count,
            i => GroupMembershipActions.FormatPlayer(group.Members[i].Player, group.Members[i].World),
            Loc.Get("Groups_Custom_Player_Remove_Tooltip"));
        if (removedMember >= 0)
            group.Members.RemoveAt(removedMember);
    }

    /// <summary>Only a real player character counts — excludes NPCs, monsters, minions, etc.</summary>
    private static bool TryGetTargetedPlayer(out string name, out string? world)
    {
        if (Plugin.TargetManager.Target is IPlayerCharacter { ObjectKind: ObjectKind.Pc } player)
        {
            name = player.Name.TextValue;
            world = player.HomeWorld.ValueNullable?.Name.ExtractText();
            return true;
        }

        name = string.Empty;
        world = null;
        return false;
    }

    private static bool TryAddPlayer(PlayerGroup group, string name, string? world)
    {
        // GroupMatcher.IsMember, not exact (name, world) equality: a bare stored entry already
        // covers this player on every world, so adding a world-qualified duplicate would only
        // bloat the config without changing what gets colored.
        if (group.Members.Any(m => GroupMatcher.IsMember(m, name, world)))
            return false;

        group.Members.Add(new GroupMember(name, world));
        return true;
    }

    private void DrawFriendGroups()
    {
        ImGuiHelpers.ScaledDummy(4f);

        using var table = ImRaii.Table("##friendGroups", 5,
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg);
        if (!table)
            return;

        ImGui.TableSetupColumn(Loc.Get("Groups_Friend_Column_Active"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Groups_Friend_Column_Name"), ImGuiTableColumnFlags.WidthFixed, 110f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn(Loc.Get("Groups_Column_NameColor"));
        ImGui.TableSetupColumn(Loc.Get("Groups_Column_NameGlow"));
        ImGui.TableSetupColumn(Loc.Get("Groups_Column_ChatTwoBackground"));
        ImGui.TableHeadersRow();

        foreach (var group in config.FriendGroups)
        {
            using var id = ImRaii.PushId(group.Id);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var active = group.Active;
            if (SettingsUi.ToggleSwitch("##active", ref active))
                group.Active = active;

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"{FriendGroupGlyph(group.FfGroup)} {Loc.Get($"Groups_FriendGroup_{group.Name}")}");

            ImGui.TableNextColumn();
            var foreground = group.Foreground;
            if (SettingsUi.RgbaColorEdit("##fg", ref foreground, allowAlpha: false))
                group.Foreground = foreground;

            ImGui.TableNextColumn();
            var glow = group.Glow;
            if (SettingsUi.RgbaColorEdit("##glow", ref glow, allowAlpha: false))
                group.Glow = glow;

            ImGui.TableNextColumn();
            DrawChatTwoBackgroundEdit(group);
        }
    }

    /// <summary>
    /// Swatch editing <see cref="PlayerGroup.ChatTwoBackground"/> (a literal RGBA value, not a
    /// UIColor row — Chat 2 draws arbitrary colors) through the same <see cref="SettingsUi.RgbaColorEdit"/>
    /// widget every other color field uses, with alpha allowed; disabled with a hint while Chat
    /// 2's styling IPC isn't connected, since only Chat 2 can render it.
    /// </summary>
    private void DrawChatTwoBackgroundEdit(PlayerGroup group)
    {
        var connected = chatTwoStyles.IsConnected;
        var background = group.ChatTwoBackground;
        using (ImRaii.Disabled(!connected))
        {
            if (SettingsUi.RgbaColorEdit("##chattwo-bg", ref background, allowAlpha: true,
                    tooltipOverride: connected ? Loc.Get("Groups_ChatTwoBackground_Tooltip") : Loc.Get("ChatTwo_NotConnected_Tooltip"),
                    defaultAlpha: 0.2f))
                group.ChatTwoBackground = background;
        }
    }

    // Matches FFXIVClientStructs' DisplayGroup order (Star..Club); not localized, these are game glyphs.
    private static string FriendGroupGlyph(int? ffGroup) => ffGroup switch
    {
        0 => "★", // Star
        1 => "●", // Circle
        2 => "▲", // Triangle
        3 => "◆", // Diamond
        4 => "♥", // Heart
        5 => "♠", // Spade
        6 => "♣", // Club
        _ => "",
    };
}
