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
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string PrimaryCommand = "/gobchat";
    private const string AliasCommand = "/gobchatex";
    private const string ShortAliasCommand = "/gex";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("GobchatEx");
    internal ChatListener ChatListener { get; init; }
    internal FriendGroupLookup FriendGroups { get; } = new();
    internal FriendListAddonListener FriendListListener { get; init; }
    private ChatTwoContextMenuIntegration ChatTwoIntegration { get; init; }
#if DEBUG
    internal ChatTwoStyleIpcTester ChatTwoStyleTester { get; init; }
#endif
    internal ChatTwoStyleProvider ChatTwoStyles { get; init; }
    private SettingsWindow SettingsWindow { get; init; }

    public Plugin()
    {
        Configuration = Configuration.Load();
        if (Configuration.Version < 1)
        {
            // v0 → v1: the RP-highlighting fields are new and simply take
            // their defaults; nothing to transform.
            Configuration.Version = 1;
            Configuration.Save();
        }

        if (Configuration.Version < 2)
        {
            // v1 → v2: HighlightChannels could have accumulated duplicate entries from a Json.NET
            // ObjectCreationHandling.Reuse deserialization bug (now fixed by the [JsonProperty]
            // attribute in Configuration.cs) — collapse any duplicates already baked into a saved
            // config.
            Configuration.HighlightChannels = [.. Configuration.HighlightChannels.Distinct()];
            Configuration.Version = 2;
            Configuration.Save();
        }

        if (Configuration.Version < 3)
        {
            // v2 → v3: seed the game's 7 fixed friend-display groups (Milestone 2). Custom Groups start
            // empty; users create those themselves in the Groups tab.
            Configuration.FriendGroups = Configuration.CreateDefaultFriendGroups();
            Configuration.Version = 3;
            Configuration.Save();
        }

        if (Configuration.Version < 5)
        {
            // v3 → v5: new default segment colors (Say soft-white 549, Emote orange 500, OOC
            // grey 4; Mention keeps 48). Only values still on the old defaults move — customized
            // colors stay untouched. Version 4 was a short-lived dev-only numbering (2026-07-05)
            // and is deliberately skipped.
            if (Configuration.SayStyle.Foreground == 1)
                Configuration.SayStyle.Foreground = 549;
            if (Configuration.EmoteStyle.Foreground == 45)
                Configuration.EmoteStyle.Foreground = 500;
            if (Configuration.OocStyle.Foreground == 500)
                Configuration.OocStyle.Foreground = 4;
            Configuration.Version = 5;
            Configuration.Save();
        }

        OnLanguageChanged(PluginInterface.UiLanguage);

        // Before SettingsWindow, which hands these to its tabs.
        ChatTwoStyles = new ChatTwoStyleProvider(Configuration, FriendGroups);
#if DEBUG
        // Debug builds only: the styling-IPC exerciser behind the Debug page. It suspends the
        // production provider while it holds Chat 2's single-provider gate.
        ChatTwoStyleTester = new ChatTwoStyleIpcTester();
        ChatTwoStyleTester.ProductionProvider = ChatTwoStyles;
#endif

        SettingsWindow = new SettingsWindow(this);
        WindowSystem.AddWindow(SettingsWindow);

        // Dalamud has no alias mechanism on CommandInfo, so each command
        // name gets its own handler pointing at the same action.
        CommandManager.AddHandler(PrimaryCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.Get("Commands_Primary_Help")
        });
        CommandManager.AddHandler(AliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.Get("Commands_Alias_Help")
        });
        CommandManager.AddHandler(ShortAliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.Get("Commands_ShortAlias_Help"),
            ShowInHelp = false
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        // Both the installer's cog and its "Open" button lead to settings —
        // the settings window is the plugin's only window.
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleSettingsUI;
        PluginInterface.LanguageChanged += OnLanguageChanged;

        ChatListener = new ChatListener(Configuration, FriendGroups);
        FriendListListener = new FriendListAddonListener(FriendGroups);
        ContextMenu.OnMenuOpened += OnMenuOpened;
        ChatTwoIntegration = new ChatTwoContextMenuIntegration(this);

        Log.Information($"{PluginInterface.Manifest.Name} loaded.");
    }

    public void Dispose()
    {
#if DEBUG
        ChatTwoStyleTester.Dispose();
#endif
        ChatTwoStyles.Dispose();
        ChatTwoIntegration.Dispose();
        ContextMenu.OnMenuOpened -= OnMenuOpened;
        FriendListListener.Dispose();
        ChatListener.Dispose();
        WindowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(PrimaryCommand);
        CommandManager.RemoveHandler(AliasCommand);
        CommandManager.RemoveHandler(ShortAliasCommand);

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
            // GobchatEx's own orange accent (see Configuration.DefaultEmoteForeground).
            PrefixColor = Configuration.DefaultEmoteForeground,
            OnClicked = clicked => OpenGroupSubmenu(clicked, name, world),
        });
    }

    private void OpenGroupSubmenu(IMenuItemClickedArgs clicked, string name, string? world)
    {
        // OpenSubmenu throws if given an empty list, so a disabled placeholder stands in for "no
        // groups configured yet" instead of just not offering the "Groups" entry at all — that way the
        // feature is discoverable (and its presence proves the hook fired) before any group exists.
        if (Configuration.Groups.Count == 0)
        {
            clicked.OpenSubmenu(Loc.Get("Groups_ContextMenu_SubmenuName"),
                [new MenuItem { Name = Loc.Get("Groups_ContextMenu_None"), IsEnabled = false }]);
            return;
        }

        var actions = new GroupMembershipActions(this, name, world);
        var items = new List<IMenuItem>(Configuration.Groups.Count);

        foreach (var group in Configuration.Groups)
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

    private void OnCommand(string command, string args)
    {
        var trimmed = args.TrimStart();
        var firstSpace = trimmed.IndexOf(' ');
        var firstWord = firstSpace < 0 ? trimmed : trimmed[..firstSpace];

        if (firstWord.Equals("group", StringComparison.OrdinalIgnoreCase)
            || firstWord.Equals("g", StringComparison.OrdinalIgnoreCase))
        {
            var rest = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..];
            GroupCommandHandler.Execute(this, rest);
            return;
        }

        ToggleSettingsUI();
    }

    private void DrawUI() => WindowSystem.Draw();
    public void ToggleSettingsUI() => SettingsWindow.Toggle();

    private void OnLanguageChanged(string langCode)
    {
        var culture = Configuration.LanguageOverride is LanguageOverride.None
            ? new CultureInfo(langCode)
            : new CultureInfo(Configuration.LanguageOverride.Code());
        Loc.Culture = culture;
    }

    /// <summary>
    /// Re-resolves Loc.Culture from the current Configuration.LanguageOverride
    /// and Dalamud's own UI language. Call after any save that could have
    /// changed LanguageOverride (SettingsWindow's footer).
    /// </summary>
    internal void RefreshLanguage() => OnLanguageChanged(PluginInterface.UiLanguage);
}
