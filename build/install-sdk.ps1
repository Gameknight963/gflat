param(
    [string]$SdkDirectory,
    [string]$Compiler = (Join-Path $PSScriptRoot '../compiler/gflat.dll')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Compiler)) {
    $Compiler = Join-Path $PSScriptRoot '../gflat/bin/Debug/net10.0/gflat.dll'
}
if (-not (Test-Path -LiteralPath $Compiler)) { throw 'Build gflat first, or pass -Compiler with the installed compiler DLL.' }
$arguments = @($Compiler, 'sdk', 'install')
if ($SdkDirectory) { $arguments += @('--sdk-directory', $SdkDirectory) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed. This explicit setup step may require administrator permissions for the selected MSBuild host.' }
