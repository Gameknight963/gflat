# Run once to configure only gflat's generated test programs, not general temp files.
param([string]$OutputDirectory = (Join-Path $env:LOCALAPPDATA 'gflat/TestOutputs'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($root) | Out-Null
$quotedRoot = "'" + $root.Replace("'", "''") + "'"
$configure = @"
`$ErrorActionPreference = 'Stop'
try {
    Add-MpPreference -ExclusionPath $quotedRoot
    if ((Get-MpPreference).ExclusionPath -notcontains $quotedRoot) {
        throw 'Defender did not retain the test-directory exclusion.'
    }
    exit 0
} catch {
    Write-Error `$_ -ErrorAction Continue
    exit 1
}
"@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($configure))
# Defender configuration requires administrator privileges. Elevate just this
# command; persist the test setting as the original user after it succeeds.
$powershell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$process = Start-Process -FilePath $powershell -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded)
if ($process.ExitCode -ne 0) { throw 'Could not configure the Defender exclusion.' }
[Environment]::SetEnvironmentVariable('GFLAT_TEST_OUTPUT_ROOT', $root, 'User')
$env:GFLAT_TEST_OUTPUT_ROOT = $root
Write-Host "Local gflat tests now use $root, with a Defender path exclusion."
