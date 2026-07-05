using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using GobchatEx.Chat;
using GobchatEx.Core;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

/// <summary>
/// Milestone 2: custom player groups (reorderable, user-named, per-group player list) plus the game's
/// seven fixed friend-list display groups (active/color only, no add/remove/rename). Mirrors
/// MentionsTab's per-item CollapsingHeader + nested flat-list-editor pattern for custom groups.
/// </summary>
internal sealed class GroupsTab : ISettingsTab
{
    public string Name => Loc.Get("Groups_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.Users;

    private readonly Configuration mutable;
    private readonly ChatTwoStyleProvider chatTwoStyles;
    private readonly UiColorPicker colorPicker = new();
    private string newGroupName = string.Empty;

    public GroupsTab(Configuration mutable, ChatTwoStyleProvider chatTwoStyles)
    {
        this.mutable = mutable;
        this.chatTwoStyles = chatTwoStyles;
    }

    public void Draw()
    {
        SettingsUi.SectionHeader(Loc.Get("Groups_Custom_Header"), Loc.Get("Groups_Custom_Header_Tooltip"));
        DrawCustomGroups();

        ImGuiHelpers.ScaledDummy(10f);

        SettingsUi.SectionHeader(Loc.Get("Groups_Friend_Header"), Loc.Get("Groups_Friend_Header_Tooltip"));
        DrawFriendGroups();
    }

    private void DrawCustomGroups()
    {
        DrawAddGroupControl();
        ImGuiHelpers.ScaledDummy(4f);

        if (mutable.Groups.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("Groups_Custom_Empty"));
            return;
        }

        var toDelete = -1;
        for (var i = 0; i < mutable.Groups.Count; ++i)
        {
            var group = mutable.Groups[i];
            using var id = ImRaii.PushId(group.Id);

            var headerLabel = group.Active
                ? group.Name
                : string.Format(Loc.Get("Groups_Custom_Inactive"), group.Name);
            if (!ImGui.CollapsingHeader(headerLabel))
                continue;

            using var indent = ImRaii.PushIndent();

            var active = group.Active;
            if (ImGui.Checkbox(Loc.Get("Groups_Custom_Active"), ref active))
                group.Active = active;

            ImGui.SameLine();
            var foreground = group.Foreground;
            if (colorPicker.Draw("fg", ref foreground, glow: false))
                group.Foreground = foreground;

            ImGui.SameLine();
            var glow = group.Glow;
            if (colorPicker.Draw("glow", ref glow, glow: true))
                group.Glow = glow;

            ImGui.SameLine();
            DrawChatTwoBackgroundEdit(group);

            ImGui.SameLine();
            var canRemove = ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift;
            bool removeClicked;
            using (ImRaii.Disabled(!canRemove))
                removeClicked = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, Loc.Get("Groups_Custom_Remove"));

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                using (ImRaii.Tooltip())
                    ImGui.TextUnformatted(Loc.Get("Groups_Custom_Remove_Tooltip"));
            }

            if (removeClicked)
            {
                toDelete = i;
                continue;
            }

            ImGuiHelpers.ScaledDummy(4f);
            ImGui.TextUnformatted(Loc.Get("Groups_Custom_Players"));
            DrawPlayers(group);
            ImGuiHelpers.ScaledDummy(6f);
        }

        if (toDelete >= 0)
            mutable.Groups.RemoveAt(toDelete);
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
        if (mutable.Groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return false;

        mutable.Groups.Add(new PlayerGroup { Id = Guid.NewGuid().ToString(), Name = name, Active = true });
        return true;
    }

    private static bool IsPureNumericName(string name) => name.Length > 0 && int.TryParse(name, out _);

    private void DrawPlayers(PlayerGroup group)
    {
        var hasTarget = TryGetTargetedPlayer(out var targetName, out var targetWorld);

        bool addClicked;
        using (ImRaii.Disabled(!hasTarget))
            addClicked = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Crosshairs, Loc.Get("Groups_Custom_AddTarget"));

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using (ImRaii.Tooltip())
                ImGui.TextUnformatted(hasTarget
                    ? string.Format(Loc.Get("Groups_Custom_AddTarget_Tooltip"), FormatMember(targetName, targetWorld))
                    : Loc.Get("Groups_Custom_NoPlayerTarget"));
        }

        if (addClicked && hasTarget)
            TryAddPlayer(group, targetName, targetWorld);

        for (var i = 0; i < group.Members.Count; ++i)
        {
            using var id = ImRaii.PushId(i);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                group.Members.RemoveAt(i);
                break;
            }

            ImGui.SameLine();
            var member = group.Members[i];
            ImGui.TextUnformatted(FormatMember(member.Player, member.World));
        }
    }

    private static string FormatMember(string name, string? world)
        => string.IsNullOrEmpty(world) ? name : $"{name} [{world}]";

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

        using var table = ImRaii.Table("##friendGroups", 5, ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(Loc.Get("Groups_Friend_Column_Active"), ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Loc.Get("Groups_Friend_Column_Name"), ImGuiTableColumnFlags.WidthFixed, 110f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Color"));
        ImGui.TableSetupColumn(Loc.Get("Formatting_Column_Glow"));
        ImGui.TableSetupColumn(Loc.Get("Groups_Column_ChatTwoBackground"));

        foreach (var group in mutable.FriendGroups.OrderBy(g => g.FfGroup))
        {
            using var id = ImRaii.PushId(group.Id);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var active = group.Active;
            if (ImGui.Checkbox("##active", ref active))
                group.Active = active;

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"{FriendGroupGlyph(group.FfGroup)} {Loc.Get($"Groups_FriendGroup_{group.Name}")}");

            ImGui.TableNextColumn();
            var foreground = group.Foreground;
            if (colorPicker.Draw("fg", ref foreground, glow: false))
                group.Foreground = foreground;

            ImGui.TableNextColumn();
            var glow = group.Glow;
            if (colorPicker.Draw("glow", ref glow, glow: true))
                group.Glow = glow;

            ImGui.TableNextColumn();
            DrawChatTwoBackgroundEdit(group);
        }
    }

    /// <summary>
    /// Swatch editing <see cref="PlayerGroup.ChatTwoBackground"/> (a literal RGBA value, not a
    /// UIColor row — Chat 2 draws arbitrary colors). Right-click clears to "no background",
    /// mirroring UiColorPicker's convention; disabled with a hint while Chat 2's styling IPC
    /// isn't connected, since only Chat 2 can render it.
    /// </summary>
    private void DrawChatTwoBackgroundEdit(PlayerGroup group)
    {
        var connected = chatTwoStyles.IsConnected;
        using (ImRaii.Disabled(!connected))
        {
            var background = RgbaColor.ToVector4(group.ChatTwoBackground);
            if (ImGui.ColorEdit4("##chattwo-bg", ref background,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf))
                group.ChatTwoBackground = RgbaColor.FromVector4(background);

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && group.ChatTwoBackground != 0)
                group.ChatTwoBackground = 0;
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using (ImRaii.Tooltip())
                ImGui.TextUnformatted(connected
                    ? Loc.Get("Groups_ChatTwoBackground_Tooltip")
                    : Loc.Get("ChatTwo_NotConnected_Tooltip"));
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
