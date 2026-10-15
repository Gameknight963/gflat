# Only used by disposable GitHub-hosted Windows jobs. Never changes a developer's
# Defender settings. Generated test programs can otherwise trigger false positives.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or
    -not $env:RUNNER_TEMP -or -not $env:GITHUB_ENV) {
    throw 'This script must run in a GitHub Actions Windows job.'
}

$root = Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) 'gflat-tests'
New-Item -ItemType Directory -Path $root -Force | Out-Null
# Fail if the exclusion cannot be configured, rather than silently losing coverage.
Add-MpPreference -ExclusionPath $root -ErrorAction Stop

# TEMP/TMP also cover compiler subprocesses (including toolchain doctor probes).
# Package tests additionally use this explicit root for their extracted fixtures.
@("GFLAT_TEST_OUTPUT_ROOT=$root", "TEMP=$root", "TMP=$root") |
    Out-File -LiteralPath $env:GITHUB_ENV -Append -Encoding utf8
Write-Host "Generated test programs will run under $root (Defender path exclusion)."
