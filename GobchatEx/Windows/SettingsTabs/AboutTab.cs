using System;
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
    private const string TsuShiUrl = "https://tsushi-illustrations.carrd.co";
    private const float LogoDisplayWidth = 180f;

    public string Name => Loc.Get("About_TabName");
    public FontAwesomeIcon Icon => FontAwesomeIcon.InfoCircle;

    private readonly Action showChangelog;

    private readonly string logoPath = Path.Combine(
        Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "GobChatEX-logo.png");

    public AboutTab(Action showChangelog)
    {
        this.showChangelog = showChangelog;
    }

    public void Draw()
    {
        ImGuiHelpers.ScaledDummy(10f);

        // Re-fetched every frame by design: ITextureProvider returns shared
        // handles and discourages caching (or disposing) the wraps.
        var logo = Plugin.TextureProvider.GetFromFile(logoPath).GetWrapOrEmpty();
        var logoScale = LogoDisplayWidth / logo.Width * ImGuiHelpers.GlobalScale;
        var logoSize = logo.Size * logoScale;
        ImGuiHelpers.CenterCursorFor(logoSize.X);
        ImGui.Image(logo.Handle, logoSize);

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
        var changelogLabel = Loc.Get("About_ViewChangelog");
        var repoLinkLabel = Loc.Get("About_RepoLink");
        var changelogWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.History, changelogLabel);
        var repoLinkWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.ExternalLinkAlt, repoLinkLabel);
        ImGuiHelpers.CenterCursorFor(changelogWidth + repoLinkWidth + ImGui.GetStyle().ItemSpacing.X);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.History, changelogLabel))
            showChangelog();
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, repoLinkLabel))
            Dalamud.Utility.Util.OpenLink(RepoUrl);

        ImGuiHelpers.ScaledDummy(10f);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGuiHelpers.CenteredText(Loc.Get("About_ArtCredit"));

        ImGuiHelpers.ScaledDummy(4f);
        var artCreditLabel = Loc.Get("About_ArtCreditLink");
        ImGuiHelpers.CenterCursorFor(
            ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Palette, artCreditLabel));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Palette, artCreditLabel))
            Dalamud.Utility.Util.OpenLink(TsuShiUrl);
    }
}
