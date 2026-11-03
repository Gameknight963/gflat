#requires -Version 5.1
<#
.SYNOPSIS
Removes script-installed SDK and Explorer integration for one Visual Studio.
.DESCRIPTION
Run in a normal PowerShell window. The SDK step requests elevation when needed.
#>
[CmdletBinding(SupportsShouldProcess)]
param([string]$VisualStudioPath,
    [Parameter(DontShow)][string]$ClassesRoot = 'Registry::HKEY_CURRENT_USER\Software\Classes')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'sdk-paths.ps1')
$instance = Get-GflatVisualStudio $VisualStudioPath -AllowMissing
$options = @{ VisualStudioPath = $instance.installationPath; WhatIf = [bool]$WhatIfPreference }
if ($PSBoundParameters.ContainsKey('Confirm')) { $options.Confirm = $PSBoundParameters.Confirm }
$step = 'SDK removal'
try {
    & (Join-Path $PSScriptRoot 'uninstall-sdk.ps1') @options
    $step = 'Explorer removal'
    & (Join-Path $PSScriptRoot 'uninstall-explorer.ps1') @options -ClassesRoot $ClassesRoot
} catch { throw "Uninstall failed during ${step}: $($_.Exception.Message). Rerun uninstall to retry." }
