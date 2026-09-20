param([string]$Package = (Join-Path $PSScriptRoot 'bin/Release/net472/gflat.VisualStudio.vsix'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$extracted = Join-Path $PSScriptRoot ('obj/package-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Package), $extracted)
$oldExecutable = $env:GFLAT_LSP_EXECUTABLE
try {
    $env:GFLAT_LSP_EXECUTABLE = Join-Path $extracted 'Server/gflat.LanguageServer.exe'
    if (-not (Test-Path -LiteralPath $env:GFLAT_LSP_EXECUTABLE)) { throw 'VSIX is missing the server executable' }
    if (-not (Test-Path -LiteralPath (Join-Path $extracted 'gflat.VisualStudio.dll'))) { throw 'VSIX is missing the language client' }
    dotnet test (Join-Path $repoRoot 'gflat.LanguageServer.Tests/gflat.LanguageServer.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged server tests failed' }
}
finally {
    $env:GFLAT_LSP_EXECUTABLE = $oldExecutable
    # Only remove the unique extraction directory underneath this project's obj folder.
    $resolved = [IO.Path]::GetFullPath($extracted)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'obj')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
