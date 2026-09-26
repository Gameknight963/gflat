param(
    [Parameter(Mandatory)][ValidateRange(1, 65535)][int]$RunNumber,
    [Parameter(Mandatory)][ValidateRange(1, 65535)][int]$Attempt
)
$ErrorActionPreference = 'Stop'
# Only used in disposable CI checkouts. No release tags or manual version bumps.
$path = Join-Path $PSScriptRoot '../editors/VisualStudio/source.extension.vsixmanifest'
[xml]$manifest = Get-Content -LiteralPath $path
$manifest.PackageManifest.Metadata.Identity.Version = "0.15.$RunNumber.$Attempt"
$manifest.Save([IO.Path]::GetFullPath($path))
