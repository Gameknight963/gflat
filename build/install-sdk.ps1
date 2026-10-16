param(
    [string]$SdkDirectory,
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
if (-not $Compiler) {
    $Compiler = @(
        (Join-Path $PSScriptRoot '../compiler/gflat.dll'),
        (Join-Path $PSScriptRoot '../gflat/bin/Debug/net10.0/gflat.dll'),
        (Join-Path $PSScriptRoot '../../Compiler/gflat.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler -or -not (Test-Path -LiteralPath $Compiler)) {
    throw 'Build gflat first, or pass -Compiler with the installed compiler executable or DLL.'
}
$arguments = @('sdk', 'install')
if ($SdkDirectory) { $arguments += @('--sdk-directory', $SdkDirectory) }
if ([IO.Path]::GetExtension($Compiler) -eq '.dll') { & dotnet $Compiler @arguments }
else { & $Compiler @arguments }
if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed. This explicit setup step may require administrator permissions for the selected MSBuild host.' }
