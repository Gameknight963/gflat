#requires -PSEdition Desktop
# Opt-in live IDE test. Uses a disposable experimental profile; never installs in
# the user's regular profile. Compiler/packet unit tests run separately in CI.
param([string]$Package = (Join-Path $PSScriptRoot 'bin/Release/net472/gflat.VisualStudio.vsix'))
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.CoreEditor -format json | ConvertFrom-Json | Select-Object -First 1
if (-not $installation) { throw 'A full Visual Studio installation is required.' }
$vs = $installation.installationPath
$ide = Join-Path $vs 'Common7/IDE/devenv.exe'
$create = Join-Path $vs 'VSSDK/VisualStudioIntegration/Tools/Bin/CreateExpInstance.exe'
if (-not (Test-Path $create)) { throw 'The Visual Studio extensibility tools are required for this live IDE test.' }
$suffix = 'GflatDebug' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$instance = ([Diagnostics.FileVersionInfo]::GetVersionInfo($ide).FileMajorPart.ToString() + '.0_' + $installation.instanceId)
$testProfilePath = Join-Path $env:LOCALAPPDATA "Microsoft/VisualStudio/$instance$suffix"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../artifacts/debugger-tests/$suffix"))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$public = Join-Path $vs 'Common7/IDE/PublicAssemblies'
$interop = Join-Path $public 'Microsoft.VisualStudio.Interop.dll'
[Reflection.Assembly]::LoadFrom($interop) | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'test-debugger.cs') -ReferencedAssemblies $interop
[GflatDebuggerTest]::RegisterFilter()

function Wait-Mode {
    param($Dte)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 200
        try { $mode = [GflatDebuggerTest]::Mode($Dte) } catch { $mode = 'pending' }
    } while (($mode -eq 'dbgRunMode' -or $mode -eq 'pending') -and [DateTime]::UtcNow -lt $deadline)
    if ($mode -eq 'pending' -or $mode -eq 'dbgRunMode') { throw "Debugger did not settle: $mode" }
    return $mode
}

$process = $null; $dte = $null; $target = $null
try {
    & $create /Reset "/VSInstance=$instance" "/RootSuffix=$suffix"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the experimental profile.' }
    $install = Start-Process (Join-Path $vs 'Common7/IDE/VSIXInstaller.exe') -WindowStyle Hidden -Wait -PassThru -ArgumentList @('/quiet', "/rootSuffix:$suffix", "/instanceIds:$($installation.instanceId)", ('"' + [IO.Path]::GetFullPath($Package) + '"'))
    if ($install.ExitCode -ne 0) { throw "Experimental VSIX installation failed: $($install.ExitCode)" }
    $update = Start-Process $ide -WindowStyle Hidden -Wait -PassThru -ArgumentList @('/RootSuffix', $suffix, '/UpdateConfiguration')
    if ($update.ExitCode -ne 0) { throw 'Experimental profile configuration failed.' }
    $manifest = Get-ChildItem (Join-Path $testProfilePath 'Extensions') -Filter extension.vsixmanifest -Recurse | Where-Object {
        [xml]$xml = Get-Content $_.FullName
        $xml.PackageManifest.Metadata.Identity.Id -eq 'gflat.LanguageSupport.72c57259-8ade-48ad-9c74-1ec89cb65515'
    } | Select-Object -First 1
    if (-not $manifest) { throw 'Experimental extension was not installed.' }
    $sdk = Join-Path $manifest.DirectoryName 'Sdk'
    $process = Start-Process $ide -WindowStyle Hidden -PassThru -ArgumentList @('/RootSuffix', $suffix, '/ResetSettings', 'General', '/Embedding', '/Log', ('"' + $root + '/activity.xml"'))
    $deadline = [DateTime]::UtcNow.AddSeconds(100)
    while (-not $dte -and [DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw 'Experimental Visual Studio exited.' }
        $dte = [GflatDebuggerTest]::Find($process.Id)
        if (-not $dte) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $dte) { throw 'Visual Studio automation was unavailable.' }

    foreach ($case in @('Stepping', 'Caught', 'Thrown', 'Rethrown', 'Unhandled')) {
        $directory = Join-Path $root $case
        New-Item -ItemType Directory -Path $directory | Out-Null
        if ($case -eq 'Stepping') {
            $source = "int main() {`n int answer = 42;`n return answer;`n}"
            $line = 3; $noStd = 'true'
        } else {
            $fail = if ($case -eq 'Rethrown') { 'try { throw new Problem(); } catch (ref Exception error) { throw; }' } else { 'throw new Problem();' }
            $main = if ($case -eq 'Unhandled') { 'Fail(); return 0;' } else { 'try { Fail(); } catch (ref Exception error) {} return 42;' }
            $source = "class Problem : Exception { public Problem() : base(`"debug message`", 17) {} }`nvoid Fail() { $fail }`nint main() { $main }"
            $line = 3; $noStd = 'false'
        }
        # Consistent LF; Set-Content's extra CRLF triggers a modal editor dialog.
        [IO.File]::WriteAllText((Join-Path $directory 'Main.gf'), $source)
        $escaped = [Security.SecurityElement]::Escape($sdk)
        [IO.File]::WriteAllText((Join-Path $directory 'probe.gfproj'), "<Project><Import Project=`"$escaped/Sdk.props`"/><PropertyGroup><NoStdLib>$noStd</NoStdLib></PropertyGroup><Import Project=`"$escaped/Sdk.targets`"/></Project>")
        [GflatDebuggerTest]::Open($dte, $directory, (Join-Path $directory 'probe.gfproj'))
        [GflatDebuggerTest]::Breakpoint($dte, (Join-Path $directory 'Main.gf'), $line)
        [GflatDebuggerTest]::Go($dte)
        if ((Wait-Mode $dte) -ne 'dbgBreakMode') { throw "$case source breakpoint did not bind." }
        $target = [Diagnostics.Process]::GetProcessById([GflatDebuggerTest]::ProcessId($dte))
        $null = $target.Handle # Keep the exit code available after process exit.
        if ($case -eq 'Stepping' -and [GflatDebuggerTest]::Value($dte, 'answer') -ne '42') { throw 'Watch did not read the initialized local.' }
        [GflatDebuggerTest]::Configure($dte, ($case -eq 'Thrown' -or $case -eq 'Rethrown'))
        [GflatDebuggerTest]::ClearBreakpoints($dte)
        [GflatDebuggerTest]::Go($dte)
        $stops = if ($case -eq 'Rethrown') { 2 } elseif ($case -eq 'Thrown' -or $case -eq 'Unhandled') { 1 } else { 0 }
        for ($i = 0; $i -lt $stops; $i++) {
            if ((Wait-Mode $dte) -ne 'dbgBreakMode') { throw "$case did not stop on exception $i." }
            $expected = if ($case -eq 'Unhandled') { 'dbgEventReasonExceptionNotHandled' } else { 'dbgEventReasonExceptionThrown' }
            if ([GflatDebuggerTest]::Reason($dte) -ne $expected) { throw "$case stopped for the wrong reason." }
            $deadline = [DateTime]::UtcNow.AddSeconds(5)
            do {
                $output = [GflatDebuggerTest]::Output($dte)
                if ($output.Contains('Problem: debug message') -and $output.Contains('Code: 17')) { break }
                Start-Sleep -Milliseconds 100
            } while ([DateTime]::UtcNow -lt $deadline)
            [IO.File]::WriteAllText((Join-Path $directory "debug-$i.log"), $output)
            if (-not $output.Contains('Problem: debug message') -or -not $output.Contains('Code: 17')) { throw 'Exception type, message, or code was lost.' }
            [GflatDebuggerTest]::Go($dte)
        }
        if ((Wait-Mode $dte) -ne 'dbgDesignMode') { throw "$case stopped unexpectedly." }
        if (-not $target.WaitForExit(10000)) { throw "$case failed to finish after continuation." }
        $expectedCode = if ($case -eq 'Unhandled') { 17 } else { 42 }
        if ($target.ExitCode -ne $expectedCode) { throw "$case exited with $($target.ExitCode), expected $expectedCode." }
        $target.Dispose(); $target = $null
        [GflatDebuggerTest]::Close($dte)
        Write-Host "PASS: $case"
    }
} finally {
    if ($target) { try { if (-not $target.HasExited) { $target.Kill() } } finally { $target.Dispose() } }
    $workers = if ($process) { @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" | Where-Object Name -eq 'MSBuild.exe') } else { @() }
    if ($dte) { try { [GflatDebuggerTest]::Quit($dte) } catch { Write-Warning $_ } }
    if ($process -and -not $process.WaitForExit(10000)) { Stop-Process -Id $process.Id -Force }
    foreach ($worker in $workers) { Stop-Process -Id $worker.ProcessId -Force -ErrorAction SilentlyContinue }
    & $create /Clean "/VSInstance=$instance" "/RootSuffix=$suffix"
    [GflatDebuggerTest]::RestoreFilter()
}
