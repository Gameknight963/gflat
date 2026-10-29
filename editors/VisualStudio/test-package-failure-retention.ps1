#requires -PSEdition Desktop
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$baseRoot = if ($env:GFLAT_TEST_OUTPUT_ROOT) { $env:GFLAT_TEST_OUTPUT_ROOT } else { Join-Path $PSScriptRoot 'obj' }
$baseRoot = [IO.Path]::GetFullPath($baseRoot)
$fixture = Join-Path $baseRoot ('package-retention-control-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$environmentNames = @('GFLAT_TEST_OUTPUT_ROOT', 'GFLAT_LSP_EXECUTABLE', 'GFLAT_EDITOR_ASSETS', 'GFLAT_HOVER_CATALOG', 'GFLAT_HOVER_CAPTURE')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $payload = Join-Path $fixture 'payload'
    [IO.Directory]::CreateDirectory($payload) | Out-Null
    $marker = 'package retention control marker'
    [IO.File]::WriteAllText((Join-Path $payload 'marker.txt'), $marker)
    $invalidPackage = Join-Path $fixture 'missing-server.vsix'
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $invalidPackage)
    $extractionRoot = Join-Path $fixture 'extractions'
    $env:GFLAT_TEST_OUTPUT_ROOT = $extractionRoot
    foreach ($name in $environmentNames | Where-Object { $_ -ne 'GFLAT_TEST_OUTPUT_ROOT' }) {
        [Environment]::SetEnvironmentVariable($name, 'retention-control-' + $name, 'Process')
    }
    $warnings = [Collections.Generic.List[string]]::new()
    $caught = $null
    try {
        & (Join-Path $PSScriptRoot 'test-package.ps1') -Package $invalidPackage 3>&1 | ForEach-Object {
            if ($_ -is [Management.Automation.WarningRecord]) { $warnings.Add($_.Message) }
        }
    }
    catch { $caught = $_ }
    if ($null -eq $caught -or $caught.Exception.Message -ne 'VSIX is missing the server executable') {
        throw "Package retention control lost the original missing-server failure: $caught"
    }
    foreach ($name in $environmentNames | Where-Object { $_ -ne 'GFLAT_TEST_OUTPUT_ROOT' }) {
        if ([Environment]::GetEnvironmentVariable($name, 'Process') -ne 'retention-control-' + $name) {
            throw "Package retention control did not restore $name"
        }
    }
    if ($env:GFLAT_TEST_OUTPUT_ROOT -ne $extractionRoot) { throw 'Package retention control changed its output root' }
    $retained = @(Get-ChildItem -LiteralPath $extractionRoot -Directory -Filter 'package-test-*')
    if ($retained.Count -ne 1) { throw "Expected one retained extraction; found $($retained.Count)" }
    if ([IO.File]::ReadAllText((Join-Path $retained[0].FullName 'marker.txt')) -ne $marker) {
        throw 'Package retention control lost the extracted marker'
    }
    if (-not ($warnings | Where-Object { $_.Contains($retained[0].FullName) })) {
        throw 'Package retention control did not expose the retained directory in its warning'
    }
}
catch {
    $_.Exception.Data['Artifacts'] = $fixture
    Write-Warning -Message "Package retention control failed; retained artifacts: $fixture" -WarningAction Continue
    throw
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
# Clean only this control's unique fixture after every assertion succeeded.
$resolved = [IO.Path]::GetFullPath($fixture)
$allowedRoot = [IO.Path]::GetFullPath($baseRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package retention control cleanup path' }
Remove-Item -LiteralPath $resolved -Recurse -Force
'Package failure retention control passed.'
