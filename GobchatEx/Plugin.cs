using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using GobchatEx.Chat;
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
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string PrimaryCommand = "/gobchat";
    private const string AliasCommand = "/gobchatex";
    private const string ShortAliasCommand = "/gex";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("GobchatEx");
    internal ChatListener ChatListener { get; init; }
    private SettingsWindow SettingsWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.Version < 1)
        {
            // v0 → v1: the RP-highlighting fields are new and simply take
            // their defaults; nothing to transform.
            Configuration.Version = 1;
            Configuration.Save();
        }

        SettingsWindow = new SettingsWindow(this);
        WindowSystem.AddWindow(SettingsWindow);

        // Dalamud has no alias mechanism on CommandInfo, so each command
        // name gets its own handler pointing at the same action.
        CommandManager.AddHandler(PrimaryCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the GobchatEx settings window."
        });
        CommandManager.AddHandler(AliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /gobchat — open the GobchatEx settings window."
        });
        CommandManager.AddHandler(ShortAliasCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /gobchat.",
            ShowInHelp = false
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        // Both the installer's cog and its "Open" button lead to settings —
        // the settings window is the plugin's only window.
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleSettingsUI;

        ChatListener = new ChatListener(Configuration);

        Log.Information($"{PluginInterface.Manifest.Name} loaded.");
    }

    public void Dispose()
    {
        ChatListener.Dispose();
        WindowSystem.RemoveAllWindows();

        CommandManager.RemoveHandler(PrimaryCommand);
        CommandManager.RemoveHandler(AliasCommand);
        CommandManager.RemoveHandler(ShortAliasCommand);

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleSettingsUI;
    }

    private void OnCommand(string command, string args) => ToggleSettingsUI();
    private void DrawUI() => WindowSystem.Draw();
    public void ToggleSettingsUI() => SettingsWindow.Toggle();
}
