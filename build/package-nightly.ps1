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
    dotnet publish (Join-Path $repo "$project/$project.csproj") -c Release -r $Runtime --self-contained false -o (Join-Path $output $folder)
    if ($LASTEXITCODE -ne 0) { throw "Publishing $project failed" }
}
# Copy tracked distribution files only, never local builds, editor state, or notes.
$files = & git -C $repo ls-files -- std build examples docs editors/shared LICENSES README.md LICENSE THIRD_PARTY_NOTICES.md
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
$compiler = Join-Path $output 'compiler/gflat.dll'
dotnet $compiler --help
if ($LASTEXITCODE -ne 0) { throw 'Packaged compiler failed to start' }
foreach ($example in @('hello', 'interpolation')) {
    dotnet $compiler build (Join-Path $output "examples/$example/$example.gfproj") --run
    if ($LASTEXITCODE -ne 0) { throw "Packaged $example project failed" }
    dotnet $compiler clean (Join-Path $output "examples/$example/$example.gfproj")
    if ($LASTEXITCODE -ne 0) { throw "Packaged $example cleanup failed" }
}
if ($Runtime -eq 'linux-x64') {
    # GitHub artifact ZIPs do not preserve executable bits; tar does.
    $archive = Join-Path (Split-Path $output) 'gflat-linux-x64.tar.gz'
    tar -czf $archive -C $output .
    if ($LASTEXITCODE -ne 0) { throw 'Linux archive creation failed' }
}
