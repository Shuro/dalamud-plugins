using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GobchatEx.Localization;

namespace GobchatEx.Windows.SettingsTabs;

internal sealed class AboutTab : ISettingsTab
{
    private const string RepoUrl = "https://github.com/Shuro/dalamud-plugins";

    public string Name => Loc.Get("About_TabName");
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
            ImGuiHelpers.CenteredText(string.Format(Loc.Get("About_ByAuthor"), Plugin.PluginInterface.Manifest.Author)
                + $" — v{Plugin.PluginInterface.Manifest.AssemblyVersion}");

        ImGuiHelpers.ScaledDummy(10f);
        using (ImRaii.TextWrapPos(0f))
            ImGui.TextUnformatted(Loc.Get("About_Description"));

        ImGuiHelpers.ScaledDummy(6f);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
        using (ImRaii.TextWrapPos(0f))
            ImGui.TextUnformatted(Loc.Get("About_Lineage"));

        ImGuiHelpers.ScaledDummy(10f);
        var repoLinkLabel = Loc.Get("About_RepoLink");
        ImGuiHelpers.CenterCursorFor(
            ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.ExternalLinkAlt, repoLinkLabel));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, repoLinkLabel))
            Dalamud.Utility.Util.OpenLink(RepoUrl);
    }
}
