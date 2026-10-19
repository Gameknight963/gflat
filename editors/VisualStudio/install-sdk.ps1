#requires -Version 5.1
<#
.SYNOPSIS
Registers the installed gflat VSIX's SDK with one Visual Studio installation.
.DESCRIPTION
Run after installing or updating the VSIX. Requests UAC elevation if needed.
Defaults to the newest installed Visual Studio. No compiler files change.
#>
[CmdletBinding(SupportsShouldProcess)]
param([string]$VisualStudioPath, [string]$SdkRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'sdk-paths.ps1')
$instance = Get-GflatVisualStudio $VisualStudioPath
if (-not $SdkRoot) { $SdkRoot = Find-GflatExtensionSdk $instance }
$SdkRoot = [IO.Path]::GetFullPath($SdkRoot)
foreach ($file in @('build/Gflat.props', 'build/Gflat.targets')) {
    if (-not (Test-Path -LiteralPath (Join-Path $SdkRoot $file) -PathType Leaf)) { throw "The gflat SDK is incomplete: $SdkRoot is missing $file" }
}
$target = Get-GflatSdkTarget $instance.installationPath
if (Test-Path -LiteralPath $target) { Assert-GflatSdkOwner $target }
if ($PSCmdlet.ShouldProcess($target, "Install gflat SDK imports pointing to $SdkRoot")) {
    try {
        [IO.Directory]::CreateDirectory((Join-Path $target 'Sdk')) | Out-Null
        @{ owner = 'gflat.VisualStudio.SDK.v1'; sdkRoot = $SdkRoot } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target '.gflat-sdk-install.json') -Encoding UTF8
        foreach ($kind in @('props', 'targets')) {
            $import = ConvertTo-GflatImportPath (Join-Path $SdkRoot "build/Gflat.$kind")
            "<Project><Import Project=`"$import`" /></Project>" | Set-Content -LiteralPath (Join-Path $target "Sdk/Sdk.$kind") -Encoding UTF8
        }
    } catch [UnauthorizedAccessException] {
        if (Start-GflatSdkElevated -ScriptPath $PSCommandPath -VisualStudioPath $instance.installationPath -SdkRoot $SdkRoot) { return }
        throw 'Cannot write to the Visual Studio installation with administrator privileges. Check its directory permissions.'
    }
    Write-Output "Installed $target -> $SdkRoot. Restart Visual Studio."
}
