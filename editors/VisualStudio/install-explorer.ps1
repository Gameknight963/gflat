#requires -Version 5.1
<#
.SYNOPSIS
Registers .gfproj files with one Visual Studio installation for the current user.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$VisualStudioPath,
    [Parameter(DontShow)][string]$ClassesRoot = 'Registry::HKEY_CURRENT_USER\Software\Classes'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'sdk-paths.ps1')
. (Join-Path $PSScriptRoot 'explorer-paths.ps1')
$registration = Get-GflatExplorerRegistration $VisualStudioPath $ClassesRoot
Assert-GflatExplorerOwner $registration
if ($PSCmdlet.ShouldProcess($registration.Key, 'Register gflat project files in Explorer')) {
    foreach ($suffix in @('', 'DefaultIcon', 'shell\open\command')) {
        $path = $registration.Key
        if ($suffix) { $path = Join-Path $path $suffix }
        if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }
    }
    Set-ItemProperty -LiteralPath $registration.Key -Name 'GflatOwner' -Value 'gflat.VisualStudio.Explorer.v1'
    Set-ItemProperty -LiteralPath $registration.Key -Name 'VisualStudioPath' -Value $registration.VisualStudioPath
    Set-Item -LiteralPath $registration.Key -Value 'gflat Project File'
    Set-Item -LiteralPath (Join-Path $registration.Key 'DefaultIcon') -Value ('"' + $registration.Executable + '",0')
    Set-Item -LiteralPath (Join-Path $registration.Key 'shell\open\command') -Value ('"' + $registration.Executable + '" "%1"')
    $openWith = Join-Path $registration.Extension 'OpenWithProgids'
    if (-not (Test-Path -LiteralPath $openWith)) { New-Item -Path $openWith -Force | Out-Null }
    New-ItemProperty -LiteralPath $openWith -Name $registration.ProgId -PropertyType String -Value '' -Force | Out-Null
    $extensionKey = Get-Item -LiteralPath $registration.Extension
    $existingDefault = $extensionKey.GetValue('')
    if (-not $existingDefault -and $ClassesRoot -eq 'Registry::HKEY_CURRENT_USER\Software\Classes' -and
        (Test-Path -LiteralPath 'Registry::HKEY_CLASSES_ROOT\.gfproj')) {
        $existingDefault = (Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\.gfproj').GetValue('')
    }
    if (-not $existingDefault) { Set-Item -LiteralPath $registration.Extension -Value $registration.ProgId }
    Update-GflatExplorerAssociations
    Write-Output "Registered .gfproj with $($registration.Executable) for the current user."
}
