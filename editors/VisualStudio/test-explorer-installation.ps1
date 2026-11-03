#requires -Version 5.1
#requires -PSEdition Desktop
param([string]$PackageDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PackageDirectory 'sdk-paths.ps1')
. (Join-Path $PackageDirectory 'explorer-paths.ps1')
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $PSScriptRoot "obj/explorer-test-$testId"
$classes = "Registry::HKEY_CURRENT_USER\Software\gflat\ExplorerTests\$testId"
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-True $rejected 'Expected the operation to fail'
}
$first = Join-Path $testRoot 'VS & first'
$second = Join-Path $testRoot 'VS second'
$sdk = Join-Path $testRoot 'Sdk'
foreach ($vs in @($first, $second)) {
    foreach ($file in @('MSBuild/Current/Bin/MSBuild.exe', 'Common7/IDE/devenv.exe')) {
        $path = Join-Path $vs $file
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        [IO.File]::WriteAllText($path, '')
    }
}
[IO.Directory]::CreateDirectory((Join-Path $sdk 'build')) | Out-Null
foreach ($kind in @('props', 'targets')) { [IO.File]::WriteAllText((Join-Path $sdk "build/Gflat.$kind"), '<Project />') }
$install = Join-Path $PackageDirectory 'install-explorer.ps1'
$remove = Join-Path $PackageDirectory 'uninstall-explorer.ps1'
$one = Get-GflatExplorerRegistration $first $classes
$two = Get-GflatExplorerRegistration $second $classes
try {
    & $install -VisualStudioPath $first -ClassesRoot $classes -WhatIf
    Assert-True (-not (Test-Path -LiteralPath $classes)) 'WhatIf changed the registry'
    & $install -VisualStudioPath $first -ClassesRoot $classes
    & $install -VisualStudioPath $first -ClassesRoot $classes
    Assert-True ((Get-Item -LiteralPath $one.Extension).GetValue('') -eq $one.ProgId) 'Initial default was not registered'
    Assert-True ((Get-Item -LiteralPath (Join-Path $one.Key 'shell/open/command')).GetValue('') -eq ('"' + $one.Executable + '" "%1"')) 'Incorrect project launch command'
    & $install -VisualStudioPath $second -ClassesRoot $classes
    Assert-True ((Get-Item -LiteralPath $one.Extension).GetValue('') -eq $one.ProgId) 'Second installation replaced the default'
    & $remove -VisualStudioPath $first -ClassesRoot $classes -WhatIf
    Assert-True (Test-Path -LiteralPath $one.Key) 'Uninstall WhatIf removed registration'
    Set-Item -LiteralPath $one.Extension -Value 'Other.Project'
    & $install -VisualStudioPath $first -ClassesRoot $classes
    Assert-True ((Get-Item -LiteralPath $one.Extension).GetValue('') -eq 'Other.Project') 'Existing default was overwritten'
    Set-ItemProperty -LiteralPath $one.Key -Name GflatOwner -Value foreign
    Assert-Rejected { & $install -VisualStudioPath $first -ClassesRoot $classes }
    Assert-Rejected { & $remove -VisualStudioPath $first -ClassesRoot $classes }
    Set-ItemProperty -LiteralPath $one.Key -Name GflatOwner -Value 'gflat.VisualStudio.Explorer.v1'
    Remove-Item -LiteralPath $one.Executable
    & $remove -VisualStudioPath $first -ClassesRoot $classes
    & $remove -VisualStudioPath $first -ClassesRoot $classes
    Assert-True (Test-Path -LiteralPath $two.Key) 'Removing one VS removed the other registration'
    Assert-True ((Get-Item -LiteralPath $one.Extension).GetValue('') -eq 'Other.Project') 'Uninstall replaced another default'
    Set-Item -LiteralPath $two.Extension -Value $two.ProgId
    & $remove -VisualStudioPath $second -ClassesRoot $classes
    Assert-True (-not (Get-Item -LiteralPath $two.Extension).GetValue('')) 'Uninstall left its default behind'

    $setup = Join-Path $PackageDirectory 'setup.ps1'
    $uninstall = Join-Path $PackageDirectory 'uninstall.ps1'
    & $setup -VisualStudioPath $second -SdkRoot $sdk -ClassesRoot $classes -WhatIf
    Assert-True (-not (Test-Path -LiteralPath $two.Key)) 'Wrapper WhatIf installed Explorer integration'
    & $setup -VisualStudioPath $second -SdkRoot $sdk -ClassesRoot $classes
    $sdkTarget = Get-GflatSdkTarget $second
    Assert-True (Test-Path -LiteralPath (Join-Path $sdkTarget 'Sdk/Sdk.props')) 'Wrapper did not install SDK'
    Assert-True (Test-Path -LiteralPath $two.Key) 'Wrapper did not install Explorer integration'
    & $uninstall -VisualStudioPath $second -ClassesRoot $classes -WhatIf
    Assert-True (Test-Path -LiteralPath $two.Key) 'Wrapper uninstall WhatIf changed registration'
    & $uninstall -VisualStudioPath $second -ClassesRoot $classes
    Assert-True (-not (Test-Path -LiteralPath $sdkTarget)) 'Wrapper left SDK files'
    Assert-True (-not (Test-Path -LiteralPath $two.Key)) 'Wrapper left Explorer registration'
    Assert-Rejected { & $setup -VisualStudioPath $second -SdkRoot (Join-Path $testRoot 'missing') -ClassesRoot $classes }
    Assert-True (Test-Path -LiteralPath $two.Key) 'Failure test did not reach SDK setup'
    & $uninstall -VisualStudioPath $second -ClassesRoot $classes
    Write-Output 'Explorer and setup wrapper checks passed: install, update, ownership, side-by-side VS, WhatIf, removal, and child failures.'
} finally {
    if (Test-Path -LiteralPath $classes) {
        $key = Get-Item -LiteralPath $classes
        if ($key.Name -ne "HKEY_CURRENT_USER\Software\gflat\ExplorerTests\$testId") { throw 'Unexpected test registry path' }
        Remove-Item -LiteralPath $classes -Recurse -Force
    }
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $expected = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "obj/explorer-test-$testId"))
    if ($resolved -ne $expected) { throw 'Unexpected test output path' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
