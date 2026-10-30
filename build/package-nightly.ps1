param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "artifacts/nightly/$Runtime" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output. Use a fresh directory." }
if (-not (Test-Path -LiteralPath (Join-Path $repo 'LICENSE'))) { throw 'Choose a project license before distributing builds.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($project in @('gflat', 'gflat.LanguageServer')) {
    $folder = if ($project -eq 'gflat') { 'compiler' } else { 'language-server' }
    dotnet publish (Join-Path $repo "$project/$project.csproj") -c Release -r $Runtime --self-contained true -o (Join-Path $output $folder)
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed" }
}
# The task is built ahead of time for both the bundled engine and VS's engine.
foreach ($framework in @('net472', 'net10.0')) {
    $destination = Join-Path $output "build/tasks/$framework"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo "build/tasks/$framework/gflat.Build.Tasks.dll") -Destination $destination
}
# Preserve the exact .NET runtime pack's redistribution notices.
$deps = Get-Content -Raw -LiteralPath (Join-Path $output 'compiler/gflat.deps.json') | ConvertFrom-Json
$runtimePack = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.Microsoft.NETCore.App.Runtime.*/*' } | Select-Object -First 1
if (-not $runtimePack) { throw 'Cannot identify bundled .NET runtime' }
$assets = Get-Content -Raw -LiteralPath (Join-Path $repo 'gflat/obj/project.assets.json') | ConvertFrom-Json
$runtimePath = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
    Join-Path $_ ($runtimePack.Substring('runtimepack.'.Length).ToLowerInvariant())
} | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $runtimePath) { throw 'Cannot find runtime license files' }
$notices = Join-Path $output 'LICENSES/dotnet'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
foreach ($file in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item -LiteralPath (Join-Path $runtimePath $file) -Destination $notices
}
# Copy tracked distribution files only, never local builds, editor state, or notes.
$files = & git -C $repo ls-files -- Standard libraries build examples docs editors/shared LICENSES README.md LICENSE THIRD_PARTY_NOTICES.md
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate distribution files' }
foreach ($file in $files) {
    if ($file -like 'build/*.ps1') { continue }
    $destination = Join-Path $output $file
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $destination
}
$commit = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit' }
@{
    commit = $commit; runtime = $Runtime; builtUtc = [DateTime]::UtcNow.ToString('o')
    workflowRun = $env:GITHUB_RUN_ID; workflowAttempt = $env:GITHUB_RUN_ATTEMPT
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'BUILD-INFO.json') -Encoding utf8

# Exercise the distributed compiler and imported project files, not checkout binaries.
$compilerName = if ($Runtime -eq 'win-x64') { 'gflat.exe' } else { 'gflat' }
$compiler = Join-Path $output "compiler/$compilerName"
& $compiler --help
if ($LASTEXITCODE -ne 0) { throw 'Packaged compiler failed to start' }
foreach ($example in @('hello', 'interpolation', 'managed', 'sort-bench')) {
    $smokeArguments = @()
    if ($env:GFLAT_TEST_OUTPUT_ROOT) {
        [IO.Directory]::CreateDirectory($env:GFLAT_TEST_OUTPUT_ROOT) | Out-Null
        $smokeArguments = @('-o', (Join-Path $env:GFLAT_TEST_OUTPUT_ROOT ("nightly-$example-" + [Guid]::NewGuid().ToString('N') + '.exe')))
    }
    $runArguments = @()
    if ($example -eq 'sort-bench') { $runArguments = @('--', '257') }
    & $compiler build (Join-Path $output "examples/$example/$example.gfproj") @smokeArguments --run @runArguments
    if ($LASTEXITCODE -ne 0) { throw "Packaged $example project failed" }
    & $compiler clean (Join-Path $output "examples/$example/$example.gfproj")
    if ($LASTEXITCODE -ne 0) { throw "Packaged $example cleanup failed" }
}
& (Join-Path $PSScriptRoot 'test-bundled-msbuild.ps1') -ToolchainDirectory $output
if ($Runtime -eq 'linux-x64') {
    # GitHub artifact ZIPs do not preserve executable bits; tar does.
    $archive = Join-Path (Split-Path $output) 'gflat-linux-x64.tar.gz'
    tar -czf $archive -C $output .
    if ($LASTEXITCODE -ne 0) { throw 'Linux archive creation failed' }
}
