<#
.SYNOPSIS
    Generates repo.json (the custom Dalamud plugin repository feed) from the
    packaged plugin manifest produced by a Release build.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$DownloadUrl,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest not found at '$ManifestPath'. Build the plugin in Release mode first."
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json

$entry = [ordered]@{
    Author              = $manifest.Author
    Name                = $manifest.Name
    Punchline           = $manifest.Punchline
    Description         = $manifest.Description
    InternalName        = $manifest.InternalName
    AssemblyVersion     = $manifest.AssemblyVersion
    RepoUrl             = $manifest.RepoUrl
    ApplicableVersion   = $manifest.ApplicableVersion
    Tags                = $manifest.Tags
    CategoryTags        = $manifest.CategoryTags
    DalamudApiLevel     = $manifest.DalamudApiLevel
    IconUrl             = $manifest.IconUrl
    IsHide              = $false
    IsTestingExclusive  = $false
    DownloadLinkInstall = $DownloadUrl
    DownloadLinkUpdate  = $DownloadUrl
    LastUpdate          = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
}

# -InputObject (not the pipeline) is required here: piping a 1-element array
# to ConvertTo-Json unrolls it, producing a bare object instead of a
# single-element JSON array, which breaks the pluginmaster.json contract.
$json = ConvertTo-Json -InputObject @($entry) -Depth 5
Set-Content -Path $OutputPath -Value $json -Encoding utf8

Write-Host "Wrote $OutputPath for $($manifest.InternalName) $($manifest.AssemblyVersion)"
