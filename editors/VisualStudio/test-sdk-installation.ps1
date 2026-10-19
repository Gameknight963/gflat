#requires -PSEdition Desktop
param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PackageDirectory 'sdk-paths.ps1')
$engine = Join-Path (Get-GflatVisualStudio).installationPath 'MSBuild/Current/Bin/MSBuild.exe'
$testRoot = Join-Path $(if ($env:GFLAT_TEST_OUTPUT_ROOT) { $env:GFLAT_TEST_OUTPUT_ROOT } else { Join-Path $PSScriptRoot 'obj' }) ('sdk-install-test-' + [Guid]::NewGuid().ToString('N'))
$fakeVs = Join-Path $testRoot 'Visual Studio'
[IO.Directory]::CreateDirectory((Join-Path $fakeVs 'MSBuild/Current/Bin')) | Out-Null
[IO.File]::WriteAllText((Join-Path $fakeVs 'MSBuild/Current/Bin/MSBuild.exe'), '')
$target = Join-Path $fakeVs 'MSBuild/Sdks/Gflat.Sdk'
$install = Join-Path $PackageDirectory 'install-sdk.ps1'
$uninstall = Join-Path $PackageDirectory 'uninstall-sdk.ps1'
$oldSdk = Join-Path $testRoot 'provider & old (1)/Sdk'
$newSdk = Join-Path $testRoot 'provider % new/Sdk'
foreach ($sdk in @($oldSdk, $newSdk)) {
    [IO.Directory]::CreateDirectory($sdk) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PackageDirectory 'Sdk/build') -Destination $sdk -Recurse
}
$project = Join-Path $testRoot 'Example.gfproj'
$compiler = [Security.SecurityElement]::Escape((Join-Path $PackageDirectory 'Compiler/gflat.exe'))
"<Project Sdk=`"Gflat.Sdk`"><PropertyGroup><NoStdLib>true</NoStdLib><GflatEmitIR>true</GflatEmitIR><GflatCompilerPath>$compiler</GflatCompilerPath></PropertyGroup></Project>" | Set-Content -LiteralPath $project
'int main() { return 0; }' | Set-Content -LiteralPath (Join-Path $testRoot 'main.gf')
function Assert-Rejected([scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation } catch { $rejected = $true }
    if (-not $rejected) { throw 'SDK setup unexpectedly accepted an unsafe operation' }
}
function Invoke-SdkMSBuild([string[]]$Arguments) {
    # Windows PowerShell turns native stderr into errors even for expected
    # failures. Capture the native exit code explicitly instead.
    $ErrorActionPreference = 'Continue'
    $log = & $engine @Arguments 2>&1
    return [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Log = ($log -join [Environment]::NewLine) }
}
function Assert-Build([string]$ExpectedSdk) {
    $resolved = Invoke-SdkMSBuild @($project, '/nologo', '/getProperty:GflatSdkRoot')
    if ($resolved.ExitCode -ne 0 -or $resolved.Log.TrimEnd('\', '/') -ne $ExpectedSdk.TrimEnd('\', '/')) { throw "SDK resolved incorrectly: $($resolved.Log)" }
    $build = Invoke-SdkMSBuild @($project, '/nologo', '/t:Build', '/v:minimal', '/nr:false')
    if ($build.ExitCode -ne 0) { throw "SDK-style build, including build-task reevaluation, failed: $($build.Log)" }
}
$oldSearchPath = $env:MSBuildSDKsPath
try {
    # Only this test process and its children use the fake VS SDK directory.
    $env:MSBuildSDKsPath = Join-Path $fakeVs 'MSBuild/Sdks'
    $result = Invoke-SdkMSBuild @($project, '/nologo', '/getProperty:GflatSdkRoot')
    if ($result.ExitCode -eq 0) { throw 'SDK unexpectedly resolved before installation' }
    & $install -VisualStudioPath $fakeVs -SdkRoot $oldSdk -WhatIf
    if (Test-Path -LiteralPath $target) { throw 'WhatIf created SDK files' }
    & $install -VisualStudioPath $fakeVs -SdkRoot $oldSdk
    & $install -VisualStudioPath $fakeVs -SdkRoot $oldSdk
    Assert-Build $oldSdk
    & $install -VisualStudioPath $fakeVs -SdkRoot $newSdk
    Assert-Build $newSdk
    & $uninstall -VisualStudioPath $fakeVs -WhatIf
    if (-not (Test-Path -LiteralPath $target)) { throw 'WhatIf removed SDK files' }
    $foreign = Join-Path $target 'Sdk/keep.txt'
    'preserve' | Set-Content -LiteralPath $foreign
    Assert-Rejected { & $uninstall -VisualStudioPath $fakeVs }
    if (-not (Test-Path -LiteralPath $foreign)) { throw 'Uninstall removed unexpected content' }
    Remove-Item -LiteralPath $foreign
    & $uninstall -VisualStudioPath $fakeVs
    & $uninstall -VisualStudioPath $fakeVs
    if (Test-Path -LiteralPath $target) { throw 'Uninstall left its SDK directory behind' }
    $result = Invoke-SdkMSBuild @($project, '/nologo', '/getProperty:GflatSdkRoot')
    if ($result.ExitCode -eq 0) { throw 'SDK still resolves after uninstall' }
    [IO.Directory]::CreateDirectory($target) | Out-Null
    $foreign = Join-Path $target 'foreign.txt'
    'preserve' | Set-Content -LiteralPath $foreign
    Assert-Rejected { & $install -VisualStudioPath $fakeVs -SdkRoot $oldSdk }
    Assert-Rejected { & $uninstall -VisualStudioPath $fakeVs }
    if (-not (Test-Path -LiteralPath $foreign)) { throw 'Setup removed an unmanaged SDK directory' }
    Write-Output 'SDK install/update/uninstall passed, including SDK-style builds and independent build-task evaluation. Only a temporary fake VS installation was modified.'
} finally {
    $env:MSBuildSDKsPath = $oldSearchPath
}
