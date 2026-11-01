#requires -PSEdition Desktop
$ErrorActionPreference = 'Stop'
$baseRoot = if ($env:GFLAT_TEST_OUTPUT_ROOT) { $env:GFLAT_TEST_OUTPUT_ROOT } else { Join-Path $PSScriptRoot 'obj' }
$baseRoot = [IO.Path]::GetFullPath($baseRoot)
$fixture = Join-Path $baseRoot ("console probe's test " + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$inputPath = Join-Path $fixture 'input.txt'
$outputPath = Join-Path $fixture 'output.txt'
$errorPath = Join-Path $fixture 'error.txt'
$runner = Join-Path $PSScriptRoot 'run-console-probe.ps1'
try {
    $fast = Join-Path $fixture 'fast.exe'
    Add-Type -OutputAssembly $fast -OutputType ConsoleApplication -TypeDefinition @'
public static class FastConsoleProbe {
    public static int Main() { return 0; }
}
'@
    [IO.File]::WriteAllText($inputPath, "Q")
    for ($index = 0; $index -lt 32; $index++) {
        & $runner -Executable $fast -InputPath $inputPath -OutputPath $outputPath -ErrorPath $errorPath
    }
    $probe = Join-Path $fixture 'probe.exe'
    Add-Type -OutputAssembly $probe -OutputType ConsoleApplication -TypeDefinition @'
public static class ConsoleProbeControl {
    public static int Main() {
        int command = System.Console.Read();
        if (command == 'T') { System.Threading.Thread.Sleep(10000); return 0; }
        if (command == 'F') {
            System.Console.Write("control stdout");
            System.Console.Error.Write("control stderr");
            return 7;
        }
        return command == 'Q' && System.Console.Read() == -1 ? 0 : 9;
    }
}
'@
    & $runner -Executable $probe -InputPath $inputPath -OutputPath $outputPath -ErrorPath $errorPath
    if ([IO.File]::ReadAllText($outputPath) -ne '' -or [IO.File]::ReadAllText($errorPath) -ne '') { throw 'Successful Console probe produced output' }
    $originalEncoding = [Console]::InputEncoding
    try {
        [Console]::InputEncoding = [Text.UTF8Encoding]::new($true)
        & $runner -Executable $probe -InputPath $inputPath -OutputPath $outputPath -ErrorPath $errorPath
        if ([Console]::InputEncoding.GetPreamble().Length -ne 3) { throw 'Console probe changed the host input encoding' }
    }
    finally { [Console]::InputEncoding = $originalEncoding }
    [IO.File]::WriteAllText($inputPath, "F")
    $failure = $null
    try { & $runner -Executable $probe -InputPath $inputPath -OutputPath $outputPath -ErrorPath $errorPath }
    catch { $failure = $_ }
    if ($null -eq $failure -or $failure.Exception.Message -notmatch 'exited with code 7.*control stdout.*control stderr') {
        throw "Console probe lost the exit code or output: $failure"
    }
    if ([IO.File]::ReadAllText($outputPath) -ne 'control stdout' -or [IO.File]::ReadAllText($errorPath) -ne 'control stderr') { throw 'Console probe did not retain diagnostic output' }
    [IO.File]::WriteAllText($inputPath, "T")
    $failure = $null
    try { & $runner -Executable $probe -InputPath $inputPath -OutputPath $outputPath -ErrorPath $errorPath -TimeoutMilliseconds 200 }
    catch { $failure = $_ }
    if ($null -eq $failure -or $failure.Exception.Message -notmatch 'timed out after 200 ms') { throw "Console probe lost its timeout: $failure" }
}
catch {
    Write-Warning "Console probe control failed. Retained artifacts: $fixture"
    throw
}
$resolved = [IO.Path]::GetFullPath($fixture)
$allowedRoot = $baseRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolved.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe Console probe cleanup path' }
Remove-Item -LiteralPath $resolved -Recurse -Force
'Console probe control passed: fast exits, exact input under UTF-8, failure diagnostics, and timeout.'
