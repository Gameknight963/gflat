#requires -PSEdition Desktop
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$VisualStudioDirectory
)
$ErrorActionPreference = 'Stop'
# Use the installed CPS types and the actual packaged settings builder, without
# starting a process or changing the user's Visual Studio startup project.
$ide = if ($VisualStudioDirectory) {
    Join-Path $VisualStudioDirectory 'Common7/IDE'
} else {
    Get-ChildItem -LiteralPath (Join-Path $env:ProgramFiles 'Microsoft Visual Studio') -Directory | ForEach-Object {
        Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object {
            $candidate = Join-Path $_.FullName 'Common7/IDE'
            if (Test-Path -LiteralPath (Join-Path $candidate 'CommonExtensions/Microsoft/Project/Microsoft.VisualStudio.ProjectSystem.VS.dll')) { $candidate }
        }
    } | Sort-Object { [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $_ 'devenv.exe')).FileMajorPart } -Descending | Select-Object -First 1
}
if (-not $ide) { Write-Host 'CPS launch SDK check unavailable: full IDE not installed.'; return }
$assemblies = @{}
$assemblyCandidates = @{}
foreach ($folder in @('CommonExtensions/Microsoft/Project', 'PrivateAssemblies', 'PublicAssemblies', '.', '../../MSBuild/Current/Bin', '../../MSBuild/Current/Bin/amd64')) {
    $candidateDirectory = Join-Path $ide $folder
    if (-not (Test-Path -LiteralPath $candidateDirectory -PathType Container)) { continue }
    foreach ($file in (Get-ChildItem -LiteralPath $candidateDirectory -Filter '*.dll' -File)) {
        if (-not $assemblies.ContainsKey($file.BaseName)) { $assemblies[$file.BaseName] = $file.FullName }
        $assemblyCandidates[$file.BaseName] = @($assemblyCandidates[$file.BaseName]) + $file.FullName
    }
}
$assemblies['netstandard'] = Join-Path $ide 'Remote Debugger/x64/netstandard.dll'
$assemblies['Microsoft.Build.Framework'] = [IO.Path]::GetFullPath((Join-Path $ide '../../MSBuild/Current/Bin/Microsoft.Build.Framework.dll'))
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $requested = [Reflection.AssemblyName]$eventArgs.Name
    $name = $requested.Name
    # MSBuild can ship a serviced dependency beside its own assemblies. Match
    # that identity before falling back to the IDE's copy of the same DLL.
    foreach ($candidate in $assemblyCandidates[$name]) {
        if ($candidate -and [Reflection.AssemblyName]::GetAssemblyName($candidate).FullName -eq $requested.FullName) {
            return [Reflection.Assembly]::LoadFrom($candidate)
        }
    }
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
    $upToDate = $client.GetType('gflat.VisualStudio.GflatBuildUpToDateCheck', $true)
    $upToDateAttributes = @($upToDate.GetCustomAttributes($false))
    if (-not ($upToDate.GetInterfaces() | Where-Object Name -eq 'IBuildUpToDateCheckProvider2') -or
        -not ($upToDateAttributes | Where-Object { $_.GetType().Name -eq 'ExportAttribute' -and $_.ContractType.Name -eq 'IBuildUpToDateCheckProvider' }) -or
        -not ($upToDateAttributes | Where-Object { $_.GetType().Name -eq 'AppliesToAttribute' -and $_.AppliesTo -eq 'Gflat' })) {
        throw 'The IDE must discover the configured project up-to-date check'
    }
    $failureTracker = $client.GetType('gflat.VisualStudio.GflatBuildFailureTracker', $true)
    if (-not ($failureTracker.GetCustomAttributes($false) | Where-Object {
        $_.GetType().Name -eq 'ExportAttribute' -and $_.ContractType.Name -eq 'IVsLoggerEventProcessor2'
    })) { throw 'Failed builds must invalidate the up-to-date state through the CPS logger' }
    $launch = $client.GetType('gflat.VisualStudio.GflatLaunch', $true)
    $targets = $client.GetType('gflat.VisualStudio.GflatDebugTargets', $true)
    $targetAttributes = @($targets.GetCustomAttributes($false))
    if (-not ($targetAttributes | Where-Object { $_.GetType().Name -eq 'ExportAttribute' -and $_.ContractType.Name -eq 'IDynamicDebugTargetsGenerator' })) { throw 'Dynamic debug target export is missing' }
    if (-not ($targetAttributes | Where-Object { $_.GetType().Name -eq 'ExportMetadataAttribute' -and $_.Name -eq 'Name' -and $_.Value -eq 'GflatDebugTargetProvider' })) { throw 'Dynamic debug target metadata is missing' }
    $makeTarget = $targets.GetMethod('Target', [Reflection.BindingFlags]'Static, NonPublic')
    foreach ($name in @('Example', 'Renamed project', 'A&B')) {
        $target = $makeTarget.Invoke($null, [object[]]@("C:\projects\$name.gfproj"))
        if ($target.Name -ne 'Project' -or $target.DisplayName -ne $name.Replace('&', '&&')) { throw 'Debug target must display the project name and escape menu mnemonics' }
        if (-not $target.IsDefault) { throw 'The project debug target must be selectable before a user preference is saved' }
    }
    $debugRule = [xml](Get-Content -Raw -LiteralPath (Join-Path $PackageDirectory 'Sdk/build/Rules/GflatLocal.xaml'))
    if ($debugRule.Rule.DynamicEnumProperty.EnumProvider -ne 'GflatDebugTargetProvider' -or
        $debugRule.Rule.DynamicEnumProperty.Name -ne 'GflatDebugTarget' -or
        $debugRule.Rule.'Rule.Metadata'.String.'#text' -ne 'GflatDebugTarget') { throw 'Debugger dropdown must use the project target provider' }
    $generalRule = [xml](Get-Content -Raw -LiteralPath (Join-Path $PackageDirectory 'Sdk/build/Rules/DebuggerGeneral.xaml'))
    if ($generalRule.Rule.'Rule.DataSource'.DataSource.SourceOfDefaultValue -ne 'AfterContext') {
        throw 'Debugger selection must read project defaults before a user preference is saved'
    }
    Add-Type -AssemblyName System.Xaml
    foreach ($schema in @('General', 'GflatLocal', 'DebuggerGeneral')) {
        $rule = [System.Xaml.XamlServices]::Load((Join-Path $PackageDirectory "Sdk/build/Rules/$schema.xaml"))
        if (-not $rule.Name) { throw "Packaged property rule $schema has no name" }
        if ($schema -eq 'GflatLocal' -and ($rule.PageTemplate -ne 'debugger' -or
            $rule.Metadata['DebugTargetDropdownEnum'] -ne 'GflatDebugTarget' -or
            -not ($rule.Properties | Where-Object Name -eq 'GflatDebugTarget'))) {
            throw 'The loaded debugger rule must expose the project launch target'
        }
    }
    $flags = [Reflection.BindingFlags]'Static, NonPublic'
    $canLaunch = $launch.GetMethod('CanLaunch', $flags)
    $create = $launch.GetMethod('CreateSettings', $flags)
    $optionsType = $canLaunch.GetParameters()[0].ParameterType
    $noDebug = [Enum]::Parse($optionsType, 'NoDebug')
    $debug = [Enum]::ToObject($optionsType, 0)
    foreach ($case in @(
        @{ Options=$noDebug; Kind='Executable'; IR=''; Expected=$true },
        @{ Options=$debug; Kind='Executable'; IR=''; Expected=$true },
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
    $debugSettings = $create.Invoke($null, [object[]]@($debug, 'Executable', '', 'app.exe', '', '', $scratch))
    if (($debugSettings.LaunchOptions -band $noDebug) -ne 0 -or $debugSettings.LaunchDebugEngineGuid -ne $nativeEngine) { throw 'F5 must launch the native debugger' }
    $console = $launch.GetMethod('ConfigureConsole', $flags)
    $integratedConsole = [Enum]::Parse($optionsType, 'IntegratedConsole')
    foreach ($options in @($debug, $noDebug)) {
        foreach ($enabled in @($false, $true)) {
            $consoleSettings = $create.Invoke($null, [object[]]@($options, 'Executable', '', 'app.exe', $arguments, '', $scratch))
            $console.Invoke($null, [object[]]@($consoleSettings, $enabled)) | Out-Null
            if ((($consoleSettings.LaunchOptions -band $integratedConsole) -ne 0) -ne $enabled -or
                ($consoleSettings.LaunchOptions -band $noDebug) -ne ($options -band $noDebug) -or
                $consoleSettings.Executable -ne (Join-Path $scratch 'app.exe') -or $consoleSettings.Arguments -ne $arguments) {
                throw 'F5 and Ctrl+F5 must honor the VS console preference and preserve the native launch'
            }
        }
    }
    $workingDirectory = Join-Path $scratch 'work'
    New-Item -ItemType Directory -Path $workingDirectory | Out-Null
    $relative = $create.Invoke($null, [object[]]@($noDebug, 'Executable', '', 'app.exe', '', 'work', $scratch))
    if ($relative.CurrentDirectory -ne $workingDirectory) { throw 'Relative working directory was not resolved from the project directory' }
    foreach ($case in @(
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
    Write-Host 'Packaged CPS F5/Ctrl+F5 provider metadata, native launch settings and diagnostics passed.'
} finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
