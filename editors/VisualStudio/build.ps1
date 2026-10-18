param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$MSBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $MSBuild) {
    $installRoot = Join-Path $env:ProgramFiles 'Microsoft Visual Studio'
    $candidates = Get-ChildItem -LiteralPath $installRoot -Directory | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object {
            $candidate = Join-Path $_.FullName 'MSBuild/Current/Bin/MSBuild.exe'
            if (Test-Path -LiteralPath $candidate) { $candidate }
        }
    }
    $MSBuild = $candidates | Sort-Object -Descending | Select-Object -First 1
}
if (-not $MSBuild -or -not (Test-Path -LiteralPath $MSBuild)) {
    throw 'Visual Studio MSBuild was not found. Pass -MSBuild with its full path.'
}
$serverOutput = Join-Path $PSScriptRoot 'obj/server'
$compilerOutput = Join-Path $PSScriptRoot 'obj/compiler'
# Publishing into an old folder can retain removed dependencies in the VSIX.
$publishRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'obj')) + [IO.Path]::DirectorySeparatorChar
foreach ($folder in @($serverOutput, $compilerOutput)) {
    $resolved = [IO.Path]::GetFullPath($folder)
    if (-not $resolved.StartsWith($publishRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish outputs must remain inside the extension obj directory' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$itemsOutput = Join-Path $PSScriptRoot 'obj/items'
New-Item -ItemType Directory -Force -Path $itemsOutput | Out-Null
$itemArchive = Join-Path $itemsOutput 'gflat-source.zip'
if (Test-Path -LiteralPath $itemArchive) { Remove-Item -LiteralPath $itemArchive }
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $PSScriptRoot 'Templates/Items/SourceFile'), $itemArchive)
dotnet publish (Join-Path $repoRoot 'gflat.LanguageServer/gflat.LanguageServer.csproj') -c $Configuration -r win-x64 --self-contained true -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw 'Language server publishing failed' }
dotnet publish (Join-Path $repoRoot 'gflat/gflat.csproj') -c $Configuration -r win-x64 --self-contained true -o $compilerOutput
if ($LASTEXITCODE -ne 0) { throw 'Compiler publishing failed' }
& (Join-Path $PSScriptRoot 'build-project-templates.ps1') -Compiler (Join-Path $PSScriptRoot 'obj/compiler/gflat.exe')
# Preserve the notices from the exact runtime pack being redistributed.
$deps = Get-Content -Raw -LiteralPath (Join-Path $serverOutput 'gflat.LanguageServer.deps.json') | ConvertFrom-Json
$runtime = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*' } | Select-Object -First 1
if (-not $runtime) { throw 'Cannot identify bundled .NET runtime' }
$assets = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'gflat.LanguageServer/obj/project.assets.json') | ConvertFrom-Json
$runtimePath = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object {
    Join-Path $_ ($runtime.Substring('runtimepack.'.Length).ToLowerInvariant())
} | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $runtimePath) { throw 'Cannot find runtime license files' }
$notices = Join-Path $PSScriptRoot 'obj/notices'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
foreach ($file in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item -LiteralPath (Join-Path $runtimePath $file) -Destination (Join-Path $notices $file)
}
$commit = & git -C $repoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source.extension.vsixmanifest')
@{ commit = $commit; builtUtc = [DateTime]::UtcNow.ToString('o'); version = $manifest.PackageManifest.Metadata.Identity.Version; workflowRun = $env:GITHUB_RUN_ID } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'obj/BUILD-INFO.json') -Encoding utf8
# VSSDK can reuse a generated manifest from an older checkout even on Rebuild.
# Regenerate these two build outputs so the packaged version follows the source.
$extensionOutputRoot = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
foreach ($relative in @("obj/$Configuration/extension.vsixmanifest", "bin/$Configuration/net472/extension.vsixmanifest")) {
    $generatedManifest = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    if (-not $generatedManifest.StartsWith($extensionOutputRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest output must remain inside the extension directory' }
    if (Test-Path -LiteralPath $generatedManifest) { Remove-Item -LiteralPath $generatedManifest }
}
& $MSBuild (Join-Path $PSScriptRoot 'gflat.VisualStudio.csproj') /restore /t:Rebuild /m /v:minimal "/p:Configuration=$Configuration"
if ($LASTEXITCODE -ne 0) { throw 'VSIX build failed' }
