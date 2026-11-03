#requires -Version 5.1
<#
.SYNOPSIS
Sets up Explorer integration and the MSBuild SDK for one Visual Studio installation.
.DESCRIPTION
Run in a normal PowerShell window. The SDK step requests elevation when needed.
#>
[CmdletBinding(SupportsShouldProcess)]
param([string]$VisualStudioPath, [string]$SdkRoot,
    [Parameter(DontShow)][string]$ClassesRoot = 'Registry::HKEY_CURRENT_USER\Software\Classes')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'sdk-paths.ps1')
$instance = Get-GflatVisualStudio $VisualStudioPath
if (-not $SdkRoot) { $SdkRoot = Find-GflatExtensionSdk $instance }
$options = @{ VisualStudioPath = $instance.installationPath; WhatIf = [bool]$WhatIfPreference }
if ($PSBoundParameters.ContainsKey('Confirm')) { $options.Confirm = $PSBoundParameters.Confirm }
$step = 'Explorer registration'
try {
    & (Join-Path $PSScriptRoot 'install-explorer.ps1') @options -ClassesRoot $ClassesRoot
    $step = 'SDK registration'
    & (Join-Path $PSScriptRoot 'install-sdk.ps1') @options -SdkRoot $SdkRoot
} catch { throw "Setup failed during ${step}: $($_.Exception.Message). Completed steps remain installed. Rerun setup to retry." }
