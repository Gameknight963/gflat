param(
    [string]$Compiler,
    [string]$Clang = 'clang',
    [string]$WindowsSdkLibraryDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) {
    throw 'This experiment requires an x64 Windows host.'
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/no-crt-windows' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null

function Invoke-Checked([string]$Tool, [string[]]$Arguments) {
    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Tool failed with exit code $LASTEXITCODE" }
}

if (-not $Compiler) {
    Invoke-Checked dotnet @('build', (Join-Path $repo 'gflat/gflat.csproj'), '--nologo', '-v:quiet')
    $Compiler = Join-Path $repo 'gflat/bin/Debug/net10.0/gflat.dll'
}
$Compiler = (Resolve-Path -LiteralPath $Compiler).Path
$clangPath = (Get-Command $Clang -CommandType Application -ErrorAction Stop).Source
$llvmDirectory = Split-Path $clangPath
$linker = Join-Path $llvmDirectory 'lld-link.exe'
$readobj = Join-Path $llvmDirectory 'llvm-readobj.exe'
$objdump = Join-Path $llvmDirectory 'llvm-objdump.exe'
foreach ($tool in @($linker, $readobj, $objdump)) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Required LLVM tool missing: $tool" }
}
if (-not $WindowsSdkLibraryDirectory) {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/Lib'
    $WindowsSdkLibraryDirectory = Get-ChildItem -LiteralPath $sdkRoot -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'um/x64' } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'kernel32.lib') } |
        Select-Object -First 1
}
if (-not $WindowsSdkLibraryDirectory) { throw 'Cannot find the Windows SDK x64 kernel32.lib.' }
$kernel32 = Join-Path $WindowsSdkLibraryDirectory 'kernel32.lib'
if (-not (Test-Path -LiteralPath $kernel32)) { throw "Import library missing: $kernel32" }

$stackProbe = Join-Path $output 'stack-probe.obj'
Invoke-Checked $clangPath @('-target', 'x86_64-pc-windows-msvc', '-c', (Join-Path $PSScriptRoot 'StackProbe.S'), '-o', $stackProbe)

# The actual project uses NoStdLib=true. Only its final native link is custom.
$baselineIr = Join-Path $output 'basic.ll'
Invoke-Checked dotnet @($Compiler, 'build', (Join-Path $PSScriptRoot 'NoCrt.gfproj'), '--emit-ir', '-o', $baselineIr)
if ((Get-Content -LiteralPath $baselineIr -Raw).Contains('@__gflat_eh_')) {
    throw 'The basic test unexpectedly requires the exception runtime.'
}
$exceptions = @('core/Exception.gf', 'core/BadAllocException.gf')
$cases = @(
    @{ Name = 'basic'; Project = $true; MustLink = $true },
    @{ Name = 'constructor'; Source = 'Constructor.gf'; Library = @(); MustLink = $true },
    @{ Name = 'heap-new'; Source = 'HeapNew.gf'; Library = $exceptions },
    @{ Name = 'throw-catch'; Source = 'ThrowCatch.gf'; Library = $exceptions },
    @{ Name = 'core-string'; Source = 'CoreString.gf'; Library = $exceptions + @('core/Text.gf', 'core/IStringConvertable.gf', 'core/Text/String.gf', 'core/Text/ByteSearch.gf') }
)
$allowedImports = @('ExitProcess', 'GetProcessHeap', 'GetStdHandle', 'HeapAlloc', 'HeapFree', 'WriteFile')
$results = @()
foreach ($case in $cases) {
    $ir = Join-Path $output ($case.Name + '.ll')
    if (-not $case.Project) {
        $inputs = @((Join-Path $PSScriptRoot 'Runtime.gf'), (Join-Path $PSScriptRoot ('probes/' + $case.Source)))
        $inputs += @($case.Library | ForEach-Object { Join-Path $repo ('std/' + $_) })
        Invoke-Checked dotnet (@($Compiler, '--no-stdlib', '--emit-ir', '-o', $ir) + $inputs)
    }
    foreach ($optimization in @(0, 2)) {
        $name = $case.Name + '-O' + $optimization
        $object = Join-Path $output ($name + '.obj')
        $executable = Join-Path $output ($name + '.exe')
        $map = Join-Path $output ($name + '.map')
        Invoke-Checked $clangPath @('-target', 'x86_64-pc-windows-msvc', '-Wno-override-module', '-c', $ir, ('-O' + $optimization), '-ffreestanding', '-fno-stack-protector', '-o', $object)
        # Only our two objects and the Windows OS import library enter this link.
        $linkArguments = @($object, $stackProbe, '/entry:gflat$Start', '/subsystem:console', '/nodefaultlib', $kernel32, ('/out:' + $executable), ('/map:' + $map))
        # Expected probe failures are recorded, including under Windows PowerShell.
        $savedPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $linkOutput = & $linker @linkArguments 2>&1 | Out-String
            $linkExitCode = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $savedPreference }
        $linkOutput | Set-Content -LiteralPath (Join-Path $output ($name + '.link.txt'))
        $result = [ordered]@{ Case = $case.Name; Optimization = $optimization; Linked = ($linkExitCode -eq 0) }
        if ($linkExitCode -ne 0) {
            $result.UndefinedSymbols = @([regex]::Matches($linkOutput, 'undefined symbol: ([^\r\n]+)') |
                ForEach-Object { $_.Groups[1].Value.Trim() } | Sort-Object -Unique)
            if ($case.MustLink) { throw "Required no-CRT case '$name' failed:`n$linkOutput" }
            if ($result.UndefinedSymbols.Count -eq 0) { throw "Unexpected linker failure:`n$linkOutput" }
        }
        else {
            $imports = & $readobj --coff-imports $executable | Out-String
            if ($LASTEXITCODE -ne 0) { throw "Cannot inspect $executable" }
            $imports | Set-Content -LiteralPath (Join-Path $output ($name + '.imports.txt'))
            $dlls = @([regex]::Matches($imports, 'Name: (\S+\.dll)', 'IgnoreCase') | ForEach-Object { $_.Groups[1].Value })
            $symbols = @([regex]::Matches($imports, 'Symbol: (\S+)') | ForEach-Object { $_.Groups[1].Value })
            if ($dlls.Count -ne 1 -or $dlls[0] -ine 'KERNEL32.dll') { throw "Unexpected DLL dependencies:`n$imports" }
            foreach ($symbol in $symbols) {
                if ($symbol -notin $allowedImports) { throw "Unexpected import: $symbol" }
            }
            if ($case.Project -and @(Compare-Object ($allowedImports | Sort-Object) ($symbols | Sort-Object)).Count -ne 0) {
                throw "The basic program's import set changed:`n$imports"
            }
            & $readobj --file-headers --unwind $executable | Set-Content -LiteralPath (Join-Path $output ($name + '.headers.txt'))
            if ($LASTEXITCODE -ne 0) { throw "Cannot inspect headers: $executable" }
            & $objdump -d $executable | Set-Content -LiteralPath (Join-Path $output ($name + '.asm.txt'))
            if ($LASTEXITCODE -ne 0) { throw "Cannot disassemble $executable" }
            $programOutput = @(& $executable)
            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) { throw "$name exited with $exitCode" }
            if ($case.Project -and ($programOutput -join "`n") -cne 'gflat without CRT') { throw "Unexpected output from $name" }
            $result.Bytes = (Get-Item -LiteralPath $executable).Length
            $result.Dlls = $dlls
            $result.Imports = $symbols
            $result.ExitCode = $exitCode
            $result.Output = $programOutput -join "`n"
        }
        $results += [pscustomobject]$result
        Write-Host "$name : $(if ($result.Linked) { 'runs without CRT' } else { 'unresolved: ' + ($result.UndefinedSymbols -join ', ') })"
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'results.json')
Write-Host "Results and disassembly: $output"
