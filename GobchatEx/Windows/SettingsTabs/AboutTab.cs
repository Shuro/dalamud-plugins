using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace GobchatEx.Windows.SettingsTabs;

internal sealed class AboutTab : ISettingsTab
{
    private const string RepoUrl = "https://github.com/Shuro/GobchatEx-plugin";
    private const string RepoLinkLabel = "Source code & issues";

    public string Name => "About";
    public FontAwesomeIcon Icon => FontAwesomeIcon.InfoCircle;

    private readonly string iconPath = Path.Combine(
        Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "icon.png");

    public void Draw()
    {
        ImGuiHelpers.ScaledDummy(10f);

        // Re-fetched every frame by design: ITextureProvider returns shared
        // handles and discourages caching (or disposing) the wraps.
        var icon = Plugin.TextureProvider.GetFromFile(iconPath).GetWrapOrEmpty();
        var iconSize = 64f * ImGuiHelpers.GlobalScale;
        ImGuiHelpers.CenterCursorFor(iconSize);
        ImGui.Image(icon.Handle, new Vector2(iconSize));

        ImGuiHelpers.ScaledDummy(6f);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGold))
            ImGuiHelpers.CenteredText(Plugin.PluginInterface.Manifest.Name);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGuiHelpers.CenteredText($"by {Plugin.PluginInterface.Manifest.Author}"
                + $" — v{Plugin.PluginInterface.Manifest.AssemblyVersion}");

        ImGuiHelpers.ScaledDummy(10f);
        using (ImRaii.TextWrapPos(0f))
            ImGui.TextUnformatted("GobchatEx highlights roleplay in the native chat log: quoted speech, "
                + "emotes, OOC and mention trigger words each get their own colors.");

        ImGuiHelpers.ScaledDummy(10f);
        ImGuiHelpers.CenterCursorFor(
            ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.ExternalLinkAlt, RepoLinkLabel));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, RepoLinkLabel))
            Dalamud.Utility.Util.OpenLink(RepoUrl);
    }
}
