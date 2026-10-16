#requires -PSEdition Desktop
param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
# Use the installed CPS types and the actual packaged settings builder, without
# starting a process or changing the user's Visual Studio startup project.
$ide = Get-ChildItem -LiteralPath (Join-Path $env:ProgramFiles 'Microsoft Visual Studio') -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object {
        $candidate = Join-Path $_.FullName 'Common7/IDE'
        if (Test-Path -LiteralPath (Join-Path $candidate 'CommonExtensions/Microsoft/Project/Microsoft.VisualStudio.ProjectSystem.VS.dll')) { $candidate }
    }
} | Sort-Object { [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $_ 'devenv.exe')).FileMajorPart } -Descending | Select-Object -First 1
if (-not $ide) { Write-Host 'CPS launch SDK check unavailable: full IDE not installed.'; return }
$assemblies = @{}
foreach ($folder in @('CommonExtensions/Microsoft/Project', 'PrivateAssemblies', 'PublicAssemblies', '.')) {
    foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $ide $folder) -Filter '*.dll' -File)) {
        if (-not $assemblies.ContainsKey($file.BaseName)) { $assemblies[$file.BaseName] = $file.FullName }
    }
}
$assemblies['netstandard'] = Join-Path $ide 'Remote Debugger/x64/netstandard.dll'
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]$eventArgs.Name).Name
    if ($assemblies.ContainsKey($name)) { return [Reflection.Assembly]::LoadFrom($assemblies[$name]) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$scratch = [IO.Path]::GetFullPath((Join-Path $packageRoot ('launch test ' + [Guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Launch test directory must stay inside the extracted package.' }
try {
    $client = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PackageDirectory 'gflat.VisualStudio.dll')))
    $provider = $client.GetType('gflat.VisualStudio.GflatLaunchProvider', $true)
    if ($provider.BaseType.FullName -ne 'Microsoft.VisualStudio.ProjectSystem.VS.Debug.DebugLaunchProviderBase') { throw 'Launch must use the CPS framework base' }
    $attributes = @($provider.GetCustomAttributes($false))
    if (-not ($attributes | Where-Object { $_.GetType().Name -eq 'ExportDebuggerAttribute' -and $_.DebuggerName -eq 'GflatLocal' })) { throw 'CPS debugger export is missing' }
    if (-not ($attributes | Where-Object { $_.GetType().Name -eq 'AppliesToAttribute' -and $_.AppliesTo -eq 'Gflat' })) { throw 'Launch export must be scoped to Gflat' }
    $launch = $client.GetType('gflat.VisualStudio.GflatLaunch', $true)
    $flags = [Reflection.BindingFlags]'Static, NonPublic'
    $canLaunch = $launch.GetMethod('CanLaunch', $flags)
    $create = $launch.GetMethod('CreateSettings', $flags)
    $optionsType = $canLaunch.GetParameters()[0].ParameterType
    $noDebug = [Enum]::Parse($optionsType, 'NoDebug')
    $debug = [Enum]::ToObject($optionsType, 0)
    foreach ($case in @(
        @{ Options=$noDebug; Kind='Executable'; IR=''; Expected=$true },
        @{ Options=$debug; Kind='Executable'; IR=''; Expected=$false },
        @{ Options=$noDebug; Kind='SourceLibrary'; IR=''; Expected=$false },
        @{ Options=$noDebug; Kind='Exe'; IR=''; Expected=$false },
        @{ Options=$noDebug; Kind='Library'; IR=''; Expected=$false },
        @{ Options=$noDebug; Kind='invalid'; IR=''; Expected=$false },
        @{ Options=$noDebug; Kind=''; IR=''; Expected=$false },
        @{ Options=$noDebug; Kind='Executable'; IR='true'; Expected=$false }
    )) {
        if ($canLaunch.Invoke($null, [object[]]@($case.Options, $case.Kind, $case.IR)) -ne $case.Expected) { throw 'Launch availability is incorrect' }
    }
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $scratch 'app.exe'), [byte[]]@(0))
    $arguments = '"a b" --option=value'
    $settings = $create.Invoke($null, [object[]]@($noDebug, 'Executable', '', 'app.exe', $arguments, '', $scratch))
    if ($settings.Executable -ne (Join-Path $scratch 'app.exe') -or $settings.CurrentDirectory -ne $scratch -or $settings.Arguments -ne $arguments) { throw 'Launch paths or arguments are incorrect' }
    if ($settings.LaunchOperation.ToString() -ne 'CreateProcess' -or ($settings.LaunchOptions -band $noDebug) -eq 0) { throw 'Launch must preserve NoDebug and use CreateProcess' }
    $nativeEngine = $optionsType.Assembly.GetType('Microsoft.VisualStudio.ProjectSystem.Debug.DebuggerEngines', $true).GetField('NativeOnlyEngine').GetValue($null)
    if ($settings.LaunchDebugEngineGuid -ne $nativeEngine) { throw 'Launch must select the SDK native engine' }
    $workingDirectory = Join-Path $scratch 'work'
    New-Item -ItemType Directory -Path $workingDirectory | Out-Null
    $relative = $create.Invoke($null, [object[]]@($noDebug, 'Executable', '', 'app.exe', '', 'work', $scratch))
    if ($relative.CurrentDirectory -ne $workingDirectory) { throw 'Relative working directory was not resolved from the project directory' }
    foreach ($case in @(
        @{ Options=$debug; Kind='Executable'; IR=''; Target='app.exe'; Directory=''; Error='NotSupportedException' },
        @{ Options=$noDebug; Kind='SourceLibrary'; IR=''; Target='app.exe'; Directory=''; Error='InvalidOperationException' },
        @{ Options=$noDebug; Kind='Exe'; IR=''; Target='app.exe'; Directory=''; Error='InvalidOperationException' },
        @{ Options=$noDebug; Kind='Library'; IR=''; Target='app.exe'; Directory=''; Error='InvalidOperationException' },
        @{ Options=$noDebug; Kind='Executable'; IR='true'; Target='app.exe'; Directory=''; Error='InvalidOperationException' },
        @{ Options=$noDebug; Kind='Executable'; IR=''; Target='missing.exe'; Directory=''; Error='FileNotFoundException' },
        @{ Options=$noDebug; Kind='Executable'; IR=''; Target='app.exe'; Directory='missing'; Error='DirectoryNotFoundException' }
    )) {
        $caught = $null
        try { $create.Invoke($null, [object[]]@($case.Options, $case.Kind, $case.IR, $case.Target, '', $case.Directory, $scratch)) | Out-Null }
        catch { $caught = $_.Exception; while ($caught.InnerException) { $caught = $caught.InnerException } }
        if (-not $caught -or $caught.GetType().Name -ne $case.Error) { throw "Missing launch diagnostic: $($case.Error)" }
    }
    foreach ($rule in @('GflatLocal.xaml', 'DebuggerGeneral.xaml')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PackageDirectory "Sdk/build/Rules/$rule"))) { throw "VSIX is missing launch rule $rule" }
    }
    Write-Host 'Packaged CPS Ctrl+F5 provider metadata, native launch settings and diagnostics passed.'
} finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
