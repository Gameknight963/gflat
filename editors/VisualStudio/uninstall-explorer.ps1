#requires -Version 5.1
<#
.SYNOPSIS
Removes the current user's script-installed .gfproj registration for one Visual Studio.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$VisualStudioPath,
    [Parameter(DontShow)][string]$ClassesRoot = 'Registry::HKEY_CURRENT_USER\Software\Classes'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'sdk-paths.ps1')
. (Join-Path $PSScriptRoot 'explorer-paths.ps1')
$registration = Get-GflatExplorerRegistration $VisualStudioPath $ClassesRoot -AllowMissing
if (-not (Test-Path -LiteralPath $registration.Key)) { Write-Output 'No script-installed Explorer registration for this Visual Studio.'; return }
Assert-GflatExplorerOwner $registration
if ($PSCmdlet.ShouldProcess($registration.Key, 'Remove gflat project registration from Explorer')) {
    $openWith = Join-Path $registration.Extension 'OpenWithProgids'
    if (Test-Path -LiteralPath $openWith) {
        Remove-ItemProperty -LiteralPath $openWith -Name $registration.ProgId -ErrorAction SilentlyContinue
    }
    if ((Test-Path -LiteralPath $registration.Extension) -and
        (Get-Item -LiteralPath $registration.Extension).GetValue('') -eq $registration.ProgId) {
        $extensionKey = (Get-Item -LiteralPath $registration.Extension).OpenSubKey('', $true)
        try { $extensionKey.DeleteValue('', $false) } finally { $extensionKey.Dispose() }
    }
    Remove-Item -LiteralPath $registration.Key -Recurse -Force
    Update-GflatExplorerAssociations
    Write-Output "Removed .gfproj registration for $($registration.VisualStudioPath)."
}
