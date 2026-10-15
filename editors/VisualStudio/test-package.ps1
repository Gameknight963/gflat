#requires -PSEdition Desktop
# The installed Visual Studio SDK checks require Windows PowerShell/.NET Framework.
param([string]$Package = (Join-Path $PSScriptRoot 'bin/Release/net472/gflat.VisualStudio.vsix'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$extracted = Join-Path $PSScriptRoot ('obj/package-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Package), $extracted)
$oldExecutable = $env:GFLAT_LSP_EXECUTABLE
$oldEditorAssets = $env:GFLAT_EDITOR_ASSETS
$oldHoverCatalog = $env:GFLAT_HOVER_CATALOG
$oldHoverCapture = $env:GFLAT_HOVER_CAPTURE
try {
    $env:GFLAT_LSP_EXECUTABLE = Join-Path $extracted 'Server/gflat.LanguageServer.exe'
    if (-not (Test-Path -LiteralPath $env:GFLAT_LSP_EXECUTABLE)) { throw 'VSIX is missing the server executable' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'gflat.VisualStudio.dll'))) { throw 'VSIX is missing the language client' }
    foreach ($asset in @('gflat.pkgdef', 'gflat.VisualStudio.pkgdef', 'gflat-language-configuration.json', 'Grammars/gflat.tmLanguage.json', 'Grammars/gflat.tmLanguage.tmTheme')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $asset))) { throw "VSIX is missing $asset" }
    }
    foreach ($asset in @('BUILD-INFO.json', 'Licenses/LICENSE', 'Licenses/MSBuild.Locator.txt', 'Licenses/dotnet/LICENSE.TXT', 'Licenses/dotnet/THIRD-PARTY-NOTICES.TXT')) {
        if (-not (Test-Path -LiteralPath (Join-Path $extracted $asset))) { throw "VSIX is missing $asset" }
    }
    foreach ($tool in @('Compiler', 'Server')) {
        foreach ($source in @('core/TypeInfo.gf', 'core/Attribute.gf', 'core/Exception.gf', 'libc/ExceptionHandling.gf', 'core/Text.gf', 'libc/Memory.gf')) {
            if (-not (Test-Path -LiteralPath (Join-Path $extracted "$tool/Std/$source"))) { throw "VSIX is missing $tool/Std/$source" }
        }
    }
    $buildInfo = Get-Content -Raw -LiteralPath (Join-Path $extracted 'BUILD-INFO.json') | ConvertFrom-Json
    if ($buildInfo.commit -notmatch '^[0-9a-f]{40}$') { throw 'VSIX source commit is missing' }
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
    if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'Microsoft.VisualStudio.VsPackage' -and $_.Path -eq 'gflat.VisualStudio.pkgdef' })) {
        throw 'VSIX does not register SDK-generated language options'
    }
    $optionRegistration = Get-Content -Raw -LiteralPath (Join-Path $extracted 'gflat.VisualStudio.pkgdef')
    if (-not $optionRegistration.Contains('Languages\Language Services\gflat\EditorToolsOptions\IntelliSense') -or
        -not $optionRegistration.Contains('{a48bdf16-daf5-4e05-92bc-f9f89c923a91}')) {
        throw 'IntelliSense options must be registered under the gflat language'
    }
    $catalog = Join-Path $extracted 'hover-icons.json'
    $capture = Join-Path $extracted 'hover-response.json'
    $nativeHoverCheck = & (Join-Path $PSScriptRoot 'test-hover.ps1') -Mode Prepare -PackageDirectory $extracted -CatalogPath $catalog -CapturePath $capture
    if ($nativeHoverCheck) {
        $env:GFLAT_HOVER_CATALOG = $catalog
        $env:GFLAT_HOVER_CAPTURE = $capture
    }
    dotnet test (Join-Path $repoRoot 'gflat.LanguageServer.Tests/gflat.LanguageServer.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged server tests failed' }
    if ($nativeHoverCheck) { & (Join-Path $PSScriptRoot 'test-hover.ps1') -Mode Validate -PackageDirectory $extracted -CatalogPath $catalog -CapturePath $capture }
    $compiler = Join-Path $extracted 'Compiler/gflat.exe'
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'VSIX is missing the project build compiler' }
    & (Join-Path $PSScriptRoot 'test-project-templates.ps1') -PackageDirectory $extracted
    & (Join-Path $PSScriptRoot 'test-launch.ps1') -PackageDirectory $extracted
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'Compiler/Runtime/Exceptions.cpp'))) { throw 'VSIX is missing the native exception runtime source' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'Compiler/Runtime/Stdio.cpp'))) { throw 'VSIX is missing the native stdio bridge source' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'Compiler/Std/core/GarbageCollector.gf'))) { throw 'VSIX is missing the managed collector contract' }
    $smoke = Join-Path $extracted 'unwinding-ref.gf'
    @'
void Set(ref int value) { value = 42; throw new Exception(); }
int main() { int value = 0; try { Set(ref value); } catch (ref Exception error) { if (value == 42) return 0; return 1; } return 2; }
'@ | Set-Content -LiteralPath $smoke
    & $compiler $smoke --run -o (Join-Path $extracted 'unwinding-ref.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged exception/ref smoke test failed' }
    $console = Join-Path $extracted 'console.gf'
    'int main() { Console::Write("console"); Console::WriteLine(); Console::Flush(); return 0; }' | Set-Content -LiteralPath $console
    $std = Join-Path $repoRoot 'std'
    $managed = Join-Path $extracted 'managed.gf'
    @'
void Drop() { int^ value = new^ int(42); }
int main() { Drop(); if (GC::Collect() != (nuint)1) return 1; int^ value = new^ int(7); GC::Collect(); return *value - 7; }
'@ | Set-Content -LiteralPath $managed
    & $compiler $managed (Join-Path $std 'libc/GC.gf') --optimization 2 --run -o (Join-Path $extracted 'managed.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged managed allocation/root/collector smoke test failed' }
    $consoleResult = & $compiler $console (Join-Path $std 'core/IOException.gf') (Join-Path $std 'core/BufferTooSmallException.gf') (Join-Path $std 'libc/Console.gf') --run -o (Join-Path $extracted 'console.exe')
    if ($LASTEXITCODE -ne 0 -or $consoleResult -ne 'console') { throw 'Packaged Console/std bridge smoke test failed' }
    $collections = Join-Path $extracted 'collections.gf'
    @'
using std;
int main() {
    Vector<String> words = new Vector<String>();
    words.Add(s"abc");
    foreach (String word in words) {
        if (word.Data == words[(nuint)0].Data) return 1;
        word[(nuint)0] = 'z';
    }
    foreach (readonly(String) word in words.AsReadOnly()) {
        if (word.Data[0] != 'a') return 2;
    }
    return 0;
}
'@ | Set-Content -LiteralPath $collections
    $bundledStd = Join-Path $extracted 'Sdk/std/core'
    & $compiler $collections (Join-Path $bundledStd 'Span.gf') (Join-Path $bundledStd 'Vector.gf') (Join-Path $bundledStd 'String.gf') --run -o (Join-Path $extracted 'collections.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged collection iteration and owning-element copy smoke test failed' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'ItemTemplates/gflat-source/SourceFile.vstemplate'))) { throw 'VSIX is missing the source item template' }
    if ((Get-Content -Raw -LiteralPath (Join-Path $extracted 'gflat.pkgdef')) -notmatch '"ProjectFactoryPackage"="\{3347BEE8-D7A1-4082-95E4-38A439553CC2\}"') { throw 'Project factory must be registered with CPS' }
    dotnet msbuild (Join-Path $repoRoot 'examples/hello/hello.gfproj') -t:Check "-p:GflatBundledCompiler=$compiler" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'MSBuild could not invoke the bundled compiler' }
    & $compiler check (Join-Path $repoRoot 'examples/hello/hello.gfproj')
    if ($LASTEXITCODE -ne 0) { throw 'Bundled compiler project check failed' }
    & $compiler build (Join-Path $repoRoot 'examples/hello/hello.gfproj') --emit-ir -o (Join-Path $extracted 'hello.ll')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extracted 'hello.ll'))) { throw 'Bundled compiler project build failed' }
    & $compiler build (Join-Path $repoRoot 'examples/interpolation/interpolation.gfproj') --emit-ir -o (Join-Path $extracted 'interpolation.ll')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extracted 'interpolation.ll'))) { throw 'Bundled compiler interpolation/std integration failed' }
    & $compiler build (Join-Path $repoRoot 'examples/values/values.gfproj') --emit-ir -o (Join-Path $extracted 'values.ll')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $extracted 'values.ll'))) { throw 'Bundled compiler owning-vector integration failed' }
}
finally {
    $env:GFLAT_LSP_EXECUTABLE = $oldExecutable
    $env:GFLAT_EDITOR_ASSETS = $oldEditorAssets
    $env:GFLAT_HOVER_CATALOG = $oldHoverCatalog
    $env:GFLAT_HOVER_CAPTURE = $oldHoverCapture
    # Only remove the unique extraction directory underneath this project's obj folder.
    $resolved = [IO.Path]::GetFullPath($extracted)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'obj')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
