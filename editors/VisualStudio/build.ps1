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
dotnet publish (Join-Path $repoRoot 'gflat.LanguageServer/gflat.LanguageServer.csproj') -c $Configuration -r win-x64 --self-contained true -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw 'Language server publishing failed' }
dotnet publish (Join-Path $repoRoot 'gflat/gflat.csproj') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $PSScriptRoot 'obj/compiler')
if ($LASTEXITCODE -ne 0) { throw 'Compiler publishing failed' }
& $MSBuild (Join-Path $PSScriptRoot 'gflat.VisualStudio.csproj') /restore /t:Rebuild /m /v:minimal "/p:Configuration=$Configuration"
if ($LASTEXITCODE -ne 0) { throw 'VSIX build failed' }
