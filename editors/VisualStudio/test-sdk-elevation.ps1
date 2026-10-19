#requires -PSEdition Desktop
param([string]$PackageDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PackageDirectory 'sdk-paths.ps1')
$actuallyAdministrator = Test-GflatAdministrator
$elevationTestState = @{}
$elevationTestState.isAdministrator = $false
$elevationTestState.launch = $null
$elevationTestState.childExitCode = 0
$elevationTestState.cancel = $false
# Intercept the UAC launch: tests must never display a real elevation prompt.
function Test-GflatAdministrator { return $elevationTestState.isAdministrator }
function Start-Process {
    param($FilePath, $Verb, $ArgumentList, $WindowStyle, [switch]$Wait, [switch]$PassThru)
    if ($elevationTestState.cancel) { throw 'User cancelled UAC' }
    $elevationTestState.launch = @{ FilePath = $FilePath; Verb = $Verb; ArgumentList = $ArgumentList; WindowStyle = $WindowStyle; Wait = $Wait; PassThru = $PassThru }
    return [PSCustomObject]@{ ExitCode = $elevationTestState.childExitCode }
}
function Assert-Rejected([scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Expected an elevation failure' }
}
$scriptPath = "C:\setup & O'Brien\install-sdk.ps1"
$vsPath = 'C:\Visual Studio (test)\'
$sdkPath = 'C:\user $data & SDK\'
if (-not (Start-GflatSdkElevated $scriptPath $vsPath $sdkPath)) { throw 'Nonadministrator did not request elevation' }
if ($elevationTestState.launch.Verb -ne 'RunAs' -or $elevationTestState.launch.WindowStyle -ne 'Normal' -or -not $elevationTestState.launch.Wait -or -not $elevationTestState.launch.PassThru) { throw 'Incorrect elevated launch settings' }
$encoded = ($elevationTestState.launch.ArgumentList -split ' ')[-1]
$command = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($encoded))
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($command, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw "Invalid elevated PowerShell command: $parseErrors" }
$call = $ast.Find({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.InvocationOperator -eq 'Ampersand' }, $true)
if ($call.CommandElements[0].Value -ne $scriptPath -or $call.CommandElements[2].Value -ne $vsPath -or $call.CommandElements[4].Value -ne $sdkPath) { throw 'Relaunch changed path arguments' }
if (-not $command.Contains("Read-Host 'Press Enter to close this window'")) { throw 'Elevated result window closes immediately' }
$elevationTestState.isAdministrator = $true; $elevationTestState.launch = $null
if (Start-GflatSdkElevated $scriptPath $vsPath $sdkPath) { throw 'Already elevated setup relaunched' }
if ($elevationTestState.launch) { throw 'Already elevated setup displayed UAC' }
$elevationTestState.isAdministrator = $false; $elevationTestState.cancel = $true
Assert-Rejected { Start-GflatSdkElevated $scriptPath $vsPath $sdkPath }
$elevationTestState.cancel = $false; $elevationTestState.childExitCode = 1
Assert-Rejected { Start-GflatSdkElevated $scriptPath $vsPath $sdkPath }
$elevationTestState.childExitCode = 0

if (-not $actuallyAdministrator) {
    # Exercise the actual install script's UnauthorizedAccessException handler,
    # using a temporary directory with write access denied to the current user.
    $root = Join-Path $(if ($env:GFLAT_TEST_OUTPUT_ROOT) { $env:GFLAT_TEST_OUTPUT_ROOT } else { Join-Path $PSScriptRoot 'obj' }) ('elevation-test-' + [Guid]::NewGuid().ToString('N'))
    $fakeVs = Join-Path $root 'Visual Studio'
    $sdks = Join-Path $fakeVs 'MSBuild/Sdks'
    $source = Join-Path $root 'Sdk'
    [IO.Directory]::CreateDirectory((Join-Path $fakeVs 'MSBuild/Current/Bin')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeVs 'MSBuild/Current/Bin/MSBuild.exe'), '')
    [IO.Directory]::CreateDirectory($sdks) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $source 'build')) | Out-Null
    foreach ($kind in @('props', 'targets')) { [IO.File]::WriteAllText((Join-Path $source "build/Gflat.$kind"), '<Project />') }
    $previousAcl = Get-Acl -LiteralPath $sdks
    $deniedAcl = Get-Acl -LiteralPath $sdks
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $deniedAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'Write', 'ContainerInherit, ObjectInherit', 'None', 'Deny'))
    try {
        Set-Acl -LiteralPath $sdks -AclObject $deniedAcl
        $elevationTestState.launch = $null
        & (Join-Path $PackageDirectory 'install-sdk.ps1') -VisualStudioPath $fakeVs -SdkRoot $source -WhatIf
        if ($elevationTestState.launch) { throw 'WhatIf displayed UAC' }
        & (Join-Path $PackageDirectory 'install-sdk.ps1') -VisualStudioPath $fakeVs -SdkRoot $source
        if (-not $elevationTestState.launch) { throw 'Access denied did not request elevation' }
    } finally { Set-Acl -LiteralPath $sdks -AclObject $previousAcl }
    & (Join-Path $PackageDirectory 'install-sdk.ps1') -VisualStudioPath $fakeVs -SdkRoot $source
    $deniedAcl = Get-Acl -LiteralPath $sdks
    $deniedAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'Delete, DeleteSubdirectoriesAndFiles', 'ContainerInherit, ObjectInherit', 'None', 'Deny'))
    try {
        Set-Acl -LiteralPath $sdks -AclObject $deniedAcl
        $elevationTestState.launch = $null
        & (Join-Path $PackageDirectory 'uninstall-sdk.ps1') -VisualStudioPath $fakeVs -WhatIf
        if ($elevationTestState.launch) { throw 'Uninstall WhatIf displayed UAC' }
        & (Join-Path $PackageDirectory 'uninstall-sdk.ps1') -VisualStudioPath $fakeVs
        if (-not $elevationTestState.launch) { throw 'Uninstall access denied did not request elevation' }
    } finally { Set-Acl -LiteralPath $sdks -AclObject $previousAcl }
    & (Join-Path $PackageDirectory 'uninstall-sdk.ps1') -VisualStudioPath $fakeVs
}
Write-Output 'SDK elevation checks passed: UAC arguments, existing elevation, cancellation, child failures, and result window. No UAC prompt was displayed.'
