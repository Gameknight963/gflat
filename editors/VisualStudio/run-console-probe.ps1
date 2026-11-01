param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$ErrorPath,
    [ValidateRange(1, 30000)][int]$TimeoutMilliseconds = 30000
)
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $Executable
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$inputBytes = [IO.File]::ReadAllBytes($InputPath)
# Process.Start retains the handle even when the child exits before we inspect it.
# .NET Framework takes stdin's encoding from Console and may emit its BOM.
# Keep the byte input exact, then restore the host's encoding immediately.
$hostInputEncoding = [Console]::InputEncoding
$hasPreamble = $hostInputEncoding.GetPreamble().Length -gt 0
try {
    if ($hasPreamble) { [Console]::InputEncoding = [Text.UTF8Encoding]::new($false) }
    $child = [Diagnostics.Process]::Start($start)
}
finally {
    if ($hasPreamble) { [Console]::InputEncoding = $hostInputEncoding }
}
try {
    $output = $child.StandardOutput.ReadToEndAsync()
    $errors = $child.StandardError.ReadToEndAsync()
    try { $child.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length) }
    catch [IO.IOException] { } # An early exit can close stdin before the write.
    try { $child.StandardInput.Close() }
    catch [IO.IOException] { }
    $timedOut = -not $child.WaitForExit($TimeoutMilliseconds)
    if ($timedOut) {
        $child.Kill()
        $child.WaitForExit()
    }
    $stdout = $output.GetAwaiter().GetResult()
    $stderr = $errors.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($OutputPath, $stdout)
    [IO.File]::WriteAllText($ErrorPath, $stderr)
    if ($timedOut) { throw "Packaged Console adapter consumer timed out after $TimeoutMilliseconds ms. stdout: $stdout stderr: $stderr" }
    if ($child.ExitCode -ne 0) {
        throw "Packaged Console adapter consumer exited with code $($child.ExitCode). stdout: $stdout stderr: $stderr"
    }
}
finally { $child.Dispose() }
