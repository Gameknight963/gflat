param([string]$Package = (Join-Path $PSScriptRoot 'bin/Release/net472/gflat.VisualStudio.vsix'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$extracted = Join-Path $PSScriptRoot ('obj/package-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Package), $extracted)
$oldExecutable = $env:GFLAT_LSP_EXECUTABLE
$oldEditorAssets = $env:GFLAT_EDITOR_ASSETS
try {
    $env:GFLAT_LSP_EXECUTABLE = Join-Path $extracted 'Server/gflat.LanguageServer.exe'
    if (-not (Test-Path -LiteralPath $env:GFLAT_LSP_EXECUTABLE)) { throw 'VSIX is missing the server executable' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'gflat.VisualStudio.dll'))) { throw 'VSIX is missing the language client' }
    foreach ($asset in @('gflat.pkgdef', 'gflat-language-configuration.json', 'Grammars/gflat.tmLanguage.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $asset))) { throw "VSIX is missing $asset" }
    }
    # Exercise the grammar and configuration actually shipped in the archive.
    Copy-Item -LiteralPath (Join-Path $extracted 'gflat-language-configuration.json') -Destination (Join-Path $extracted 'Grammars')
    $env:GFLAT_EDITOR_ASSETS = Join-Path $extracted 'Grammars'
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $extracted 'extension.vsixmanifest')
    [xml]$sourceManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source.extension.vsixmanifest')
    if ($manifest.PackageManifest.Metadata.Identity.Version -ne $sourceManifest.PackageManifest.Metadata.Identity.Version) {
        throw 'Packaged manifest is stale. Rebuild the VSIX.'
    }
    if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'Microsoft.VisualStudio.VsPackage' -and $_.Path -eq 'gflat.pkgdef' })) {
        throw 'VSIX does not register the editor package definition'
    }
    dotnet test (Join-Path $repoRoot 'gflat.LanguageServer.Tests/gflat.LanguageServer.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged server tests failed' }
    $compiler = Join-Path $extracted 'Compiler/gflat.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'VSIX is missing the project build compiler' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'ItemTemplates/gflat-source/SourceFile.vstemplate'))) { throw 'VSIX is missing the source item template' }
    if ((Get-Content -Raw -LiteralPath (Join-Path $extracted 'gflat.pkgdef')) -notmatch '"ProjectFactoryPackage"="\{3347BEE8-D7A1-4082-95E4-38A439553CC2\}"') { throw 'Project factory must be registered with CPS' }
    dotnet msbuild (Join-Path $repoRoot 'examples/hello/hello.gfproj') -t:Check "-p:GflatBundledCompiler=$compiler" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'MSBuild could not invoke the bundled compiler' }
    & $compiler check (Join-Path $repoRoot 'examples/hello/hello.gfproj')
    if ($LASTEXITCODE -ne 0) { throw 'Bundled compiler project check failed' }
    & $compiler build (Join-Path $repoRoot 'examples/hello/hello.gfproj') --emit-ir -o (Join-Path $extracted 'hello.ll')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extracted 'hello.ll'))) { throw 'Bundled compiler project build failed' }
}
finally {
    $env:GFLAT_LSP_EXECUTABLE = $oldExecutable
    $env:GFLAT_EDITOR_ASSETS = $oldEditorAssets
    # Only remove the unique extraction directory underneath this project's obj folder.
    $resolved = [IO.Path]::GetFullPath($extracted)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'obj')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
