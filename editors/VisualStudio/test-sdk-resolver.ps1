#requires -PSEdition Desktop
param(
    [string]$PackageDirectory,
    [string]$Package = (Join-Path $PSScriptRoot 'bin/Release/net472/gflat.VisualStudio.vsix')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$testRoot = Join-Path $PSScriptRoot ('obj/sdk-resolver-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $testRoot 'package'
    [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Package), $PackageDirectory)
}
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
foreach ($file in @('gflat.VisualStudio.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.Core.dll', 'MonoMod.Utils.dll', 'Mono.Cecil.dll', 'MonoMod.Iced.dll', 'Sdk/Sdk.props', 'Sdk/Sdk.targets')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory $file))) { throw "VSIX is missing $file" }
}
# Run separate processes: both engines have the same assembly identity and must
# not share an AppDomain. Test the actual extension DLL extracted from the VSIX.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installations = & $vswhere -all -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if (-not $installations) { throw 'No Visual Studio MSBuild installation found' }
foreach ($installation in $installations) {
    $bin = Join-Path $installation 'MSBuild/Current/Bin'
    $engine = Join-Path $bin 'amd64'
    $output = Join-Path $testRoot ([IO.Path]::GetFileName((Split-Path $installation)) + '-resolver-test.exe')
    & (Join-Path $bin 'Roslyn/csc.exe') /nologo /target:exe /platform:x64 "/out:$output" "/reference:$engine/Microsoft.Build.dll" "/reference:$engine/Microsoft.Build.Framework.dll" (Join-Path $PSScriptRoot 'test-sdk-resolver.cs')
    if ($LASTEXITCODE -ne 0) { throw 'SDK resolver test compilation failed' }
    & $output $engine $PackageDirectory $testRoot
    if ($LASTEXITCODE -ne 0) { throw "SDK resolver tests failed for $installation" }
}
