using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using GobchatEx.Chat;
using GobchatEx.Config;
using GobchatEx.Localization;
using GobchatEx.Windows;

namespace GobchatEx;

public sealed class Plugin : IDalamudPlugin
{
    // ------------------------------------------------------------------
    // Service injection (Idiom A: static [PluginService] properties).
    // Accessible from anywhere via Plugin.ChatGui, Plugin.PartyList, etc.
    // Add or remove based on the plugin's actual needs — unused services
    // still go through the IoC container and clutter the file.
    // ------------------------------------------------------------------
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private const string PrimaryCommand = "/gex";
    private const string AliasCommand = "/gobchatex";
    private const string LegacyAliasCommand = "/gobchat";

    // Dalamud's native context-menu Prefix/PrefixColor is UIColor-sheet-row-only (an
    // ImGui-side render, not a SeString we control) — unlike FormattingConfig's colors, which
    // moved to packed RGBA rendered via our own raw Color/EdgeColor macros. Row 500 is the same
    // orange hue as FormattingConfig.DefaultEmoteForeground, kept here as its own constant since
    // the two can no longer share one value.
    private const ushort SubmenuPrefixColorRow = 500;

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("GobchatEx");
    internal ChatListener ChatListener { get; init; }
    internal ChatLogger ChatLogger { get; init; }
    internal LegacyCommandListener LegacyCommandListener { get; init; }
    internal FriendGroupLookup FriendGroups { get; } = new();
    // Shared by the chat handler and the Mentions tab's preview button, so
    // both play through one NAudio pipeline. Created before SettingsWindow
    // (which hands it to MentionsTab); disposed after ChatListener.
    internal SoundPlayer SoundPlayer { get; } = new();
    internal FriendListAddonListener FriendListListener { get; init; }
    // Written by ChatListener's chat pass, shown by MentionHistoryWindow; in-memory only.
    internal MentionHistory MentionHistory { get; } = new();
    // The Range tab's transient in-game range preview. Created before SettingsWindow (which
    // hands it to RangeTab); drawn from DrawUI, no state to persist or dispose.
    internal RangeRingsOverlay RangeRings { get; } = new();
    private ChatTwoContextMenuIntegration ChatTwoIntegration { get; init; }
#if DEBUG
    internal ChatTwoStyleIpcTester ChatTwoStyleTester { get; init; }
#endif
    internal ChatTwoStyleProvider ChatTwoStyles { get; init; }
    private SettingsWindow SettingsWindow { get; init; }
    private QuickbarWindow QuickbarWindow { get; init; }
    private MentionHistoryWindow MentionHistoryWindow { get; init; }

    public Plugin()
    {
        Configuration = Configuration.Load();

        OnLanguageChanged(PluginInterface.UiLanguage);

        // Before SettingsWindow, which hands these to its tabs.
        ChatTwoStyles = new ChatTwoStyleProvider(Configuration, FriendGroups);
        ChatLogger = new ChatLogger(Configuration.ChatLog);
#if DEBUG
        // Debug builds only: the styling-IPC exerciser behind the Debug page. It suspends the
        // production provider while it holds Chat 2's single-provider gate.
        ChatTwoStyleTester = new ChatTwoStyleIpcTester();
        ChatTwoStyleTester.ProductionProvider = ChatTwoStyles;
#endif

        SettingsWindow = new SettingsWindow(this);
        WindowSystem.AddWindow(SettingsWindow);
        QuickbarWindow = new QuickbarWindow(this);
        WindowSystem.AddWindow(QuickbarWindow);
        MentionHistoryWindow = new MentionHistoryWindow(this);
        WindowSystem.AddWindow(MentionHistoryWindow);

        // Dalamud has no alias mechanism on CommandInfo, so each command
        // name gets its own handler pointing at the same action. DisplayOrder
        // pins /gex first in the installer's command list regardless of
        // alphabetical sort.
        CommandManager.AddHandler(PrimaryCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.Get("Commands_Primary_Help"),
            DisplayOrder = 0
        });
        CommandManager.AddHandler(AliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.Get("Commands_Alias_Help"),
            DisplayOrder = 1
        });
        CommandManager.AddHandler(LegacyAliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.Get("Commands_Alias_Help"),
            DisplayOrder = 2
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        // Both the installer's cog and its "Open" button lead to settings —
        // the Quickbar overlay manages its own visibility via config.
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleSettingsUI;
        PluginInterface.LanguageChanged += OnLanguageChanged;

        ChatListener = new ChatListener(Configuration, FriendGroups, SoundPlayer, MentionHistory);
        LegacyCommandListener = new LegacyCommandListener(this);
        FriendListListener = new FriendListAddonListener(FriendGroups);
        ContextMenu.OnMenuOpened += OnMenuOpened;
        ChatTwoIntegration = new ChatTwoContextMenuIntegration(this);

        Log.Information($"{PluginInterface.Manifest.Name} loaded.");
    }

    public void Dispose()
    {
        // WindowSystem.RemoveAllWindows never fires OnClose, so commit any
        // settings edit still inside its debounce window here — before the
        // ChatListener/ChatTwoStyles consumers a commit notifies are disposed.
        // Guarded so a failed commit can't abort the rest of the teardown.
        try
        {
            SettingsWindow.CommitIfChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to commit pending settings edits during dispose.");
        }

#if DEBUG
        ChatTwoStyleTester.Dispose();
#endif
        ChatTwoStyles.Dispose();
        ChatTwoIntegration.Dispose();
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        FriendListListener.Dispose();
        LegacyCommandListener.Dispose();
        ChatListener.Dispose();
        ChatLogger.Dispose();
        SoundPlayer.Dispose();
        WindowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(PrimaryCommand);
        CommandManager.RemoveHandler(AliasCommand);
        CommandManager.RemoveHandler(LegacyAliasCommand);

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleSettingsUI;
        PluginInterface.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>
    /// Adds a "Groups" submenu entry to any native right-click menu targeting a player name (chat log,
    /// party list, target, friend list, ...) — not restricted to a specific addon. An earlier version
    /// filtered on <c>args.AddonName</c> being "ChatLog"/"ChatLogPanel_N", which turned out to be an
    /// unverified guess that excluded the real chat log addon name entirely (confirmed by other
    /// plugins' menu items appearing on the exact same right-click while ours never did). Restricting
    /// by addon was never load-bearing anyway — a player-name context menu is a player-name context
    /// menu regardless of where it was opened from. The Chat 2 plugin draws its own separate context
    /// menu entirely outside this addon-based hook — see ChatTwoContextMenuIntegration for that surface.
    /// </summary>
    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault target || string.IsNullOrEmpty(target.TargetName))
            return;

        var name = target.TargetName;
        var world = target.TargetHomeWorld.ValueNullable?.Name.ExtractText();

        args.AddMenuItem(new MenuItem
        {
            Name = Loc.Get("Groups_ContextMenu_SubmenuName"),
            IsSubmenu = true,
            // Boxed "G" for GobchatEx. Silences Dalamud's "no prefix" warning (ContextMenu.cs falls
            // back to its own default + logs otherwise).
            Prefix = SeIconChar.BoxedLetterG,
            // GobchatEx's own orange accent (see SubmenuPrefixColorRow above).
            PrefixColor = SubmenuPrefixColorRow,
            OnClicked = clicked => OpenGroupSubmenu(clicked, name, world),
        });
    }

    private void OpenGroupSubmenu(IMenuItemClickedArgs clicked, string name, string? world)
    {
        // OpenSubmenu throws if given an empty list, so a disabled placeholder stands in for "no
        // groups configured yet" instead of just not offering the "Groups" entry at all — that way the
        // feature is discoverable (and its presence proves the hook fired) before any group exists.
        if (Configuration.Groups.Groups.Count == 0)
        {
            clicked.OpenSubmenu(Loc.Get("Groups_ContextMenu_SubmenuName"),
                [new MenuItem { Name = Loc.Get("Groups_ContextMenu_None"), IsEnabled = false }]);
            return;
        }

        var actions = new GroupMembershipActions(this, name, world);
        var items = new List<IMenuItem>(Configuration.Groups.Groups.Count);

        foreach (var group in Configuration.Groups.Groups)
        {
            var inGroup = actions.IsInGroup(group);
            items.Add(new MenuItem
            {
                Name = inGroup
                    ? string.Format(Loc.Get("Groups_ContextMenu_RemoveFrom"), group.Name)
                    : string.Format(Loc.Get("Groups_ContextMenu_AddTo"), group.Name),
                OnClicked = _ =>
                {
                    if (inGroup)
                        actions.RemoveFromGroup(group);
                    else
                        actions.AddToGroup(group);
                },
            });
        }

        clicked.OpenSubmenu(Loc.Get("Groups_ContextMenu_SubmenuName"), items);
    }

    private void OnCommand(string command, string args) => CommandDispatcher.Execute(this, args);

    private void DrawUI()
    {
        WindowSystem.Draw();
        RangeRings.Draw();
    }
    public void ToggleSettingsUI() => SettingsWindow.Toggle();

    /// <summary>Toggles the recent-mentions window (Milestone 7) — the Quickbar's bell button.</summary>
    internal void ToggleMentionHistory() => MentionHistoryWindow.Toggle();

    /// <summary>Opens (never closes) and focuses the settings window — the Quickbar's cog.</summary>
    public void OpenSettingsUI()
    {
        SettingsWindow.IsOpen = true;
        SettingsWindow.BringToFront();
    }

    private void OnLanguageChanged(string langCode)
    {
        var culture = Configuration.General.LanguageOverride is LanguageOverride.None
            ? new CultureInfo(langCode)
            : new CultureInfo(Configuration.General.LanguageOverride.Code());
        Loc.Culture = culture;
    }

    /// <summary>
    /// Re-resolves Loc.Culture from the current Configuration.General.LanguageOverride
    /// and Dalamud's own UI language. Call after any save that could have
    /// changed LanguageOverride (SettingsWindow's commit).
    /// </summary>
    internal void RefreshLanguage() => OnLanguageChanged(PluginInterface.UiLanguage);
}
