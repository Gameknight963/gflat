#requires -Version 5.1
<#
.SYNOPSIS
Removes the SDK imports created by install-sdk.ps1 for one Visual Studio.
#>
[CmdletBinding(SupportsShouldProcess)]
param([string]$VisualStudioPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'sdk-paths.ps1')
$instance = Get-GflatVisualStudio $VisualStudioPath
$target = Get-GflatSdkTarget $instance.installationPath
if (-not (Test-Path -LiteralPath $target)) { Write-Output "No script-installed gflat SDK at $target."; return }
Assert-GflatSdkOwner $target
# Delete only our known files. Unexpected content stays untouched.
foreach ($entry in Get-ChildItem -LiteralPath $target -Force) {
    if ($entry.Name -notin @('Sdk', '.gflat-sdk-install.json')) { throw "Unexpected SDK content; nothing removed: $($entry.FullName)" }
}
$sdk = Join-Path $target 'Sdk'
if (Test-Path -LiteralPath $sdk) {
    foreach ($entry in Get-ChildItem -LiteralPath $sdk -Force) {
        if ($entry.PSIsContainer -or $entry.Name -notin @('Sdk.props', 'Sdk.targets')) { throw "Unexpected SDK content; nothing removed: $($entry.FullName)" }
    }
}
if ($PSCmdlet.ShouldProcess($target, 'Remove script-installed gflat SDK imports')) {
    try {
        foreach ($file in @('Sdk/Sdk.props', 'Sdk/Sdk.targets')) {
            $path = Join-Path $target $file
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        if (Test-Path -LiteralPath $sdk) { [IO.Directory]::Delete($sdk) }
        Remove-Item -LiteralPath (Join-Path $target '.gflat-sdk-install.json') -Force
        [IO.Directory]::Delete($target)
    } catch [UnauthorizedAccessException] {
        throw 'Cannot write to the Visual Studio installation. Run this script from an elevated PowerShell window.'
    }
    Write-Output "Removed $target. Restart Visual Studio."
}
