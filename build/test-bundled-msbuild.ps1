param(
    [Parameter(Mandatory)][string]$ToolchainDirectory,
    [string]$CompilerDirectory
)
$ErrorActionPreference = 'Stop'
$toolchain = [IO.Path]::GetFullPath($ToolchainDirectory)
if (-not $CompilerDirectory) { $CompilerDirectory = Join-Path $toolchain 'compiler' }
$compilerDirectory = [IO.Path]::GetFullPath($CompilerDirectory)
$compilerName = if ([IO.Path]::DirectorySeparatorChar -eq '\') { 'gflat.exe' } else { 'gflat' }
$compiler = Join-Path $compilerDirectory $compilerName
foreach ($file in @($compiler, (Join-Path $compilerDirectory 'MSBuild.dll'), (Join-Path $compilerDirectory 'Sdks/Gflat.Sdk/Sdk/Sdk.props'), (Join-Path $compilerDirectory 'Sdks/Gflat.Sdk/Sdk/Sdk.targets'), (Join-Path $toolchain 'build/tasks/net10.0/gflat.Build.Tasks.dll'))) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Bundle is missing $file" }
}
$testBase = if ($env:GFLAT_TEST_OUTPUT_ROOT) { $env:GFLAT_TEST_OUTPUT_ROOT } else { [IO.Path]::GetTempPath() }
$testBase = [IO.Path]::GetFullPath($testBase).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$scratch = [IO.Path]::GetFullPath((Join-Path $testBase ('gflat bundled msbuild ' + [Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$savedEnvironment = @{}
foreach ($name in @('PATH', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_MULTILEVEL_LOOKUP', 'MSBUILD_EXE_PATH', 'MSBuildExtensionsPath', 'MSBuildSDKsPath', 'GflatSdkRoot')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
function Invoke-Compiler {
    & $compiler @args
    if ($LASTEXITCODE -ne 0) { throw "Bundled compiler failed ($LASTEXITCODE): $args" }
}
try {
    # Unix MSBuild Exec starts sh by name. Keep only that shell available;
    # retaining /bin or /usr/bin would also expose an installed dotnet/MSBuild.
    if ([IO.Path]::DirectorySeparatorChar -ne '\') {
        $shell = Get-Command sh -CommandType Application -ErrorAction Stop | Select-Object -First 1
        [IO.File]::CreateSymbolicLink((Join-Path $scratch 'sh'), $shell.Source) | Out-Null
    }
    # No dotnet, SDK, or installed MSBuild may be used by this process or its children.
    $env:PATH = $scratch
    foreach ($command in @('dotnet', 'msbuild')) {
        if (Get-Command $command -CommandType Application -ErrorAction SilentlyContinue) {
            throw "Isolation test still exposes $command on PATH"
        }
    }
    if ([IO.Path]::DirectorySeparatorChar -ne '\' -and
        -not (Get-Command sh -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Isolation test removed the shell required by MSBuild Exec'
    }
    $env:DOTNET_ROOT = Join-Path $scratch 'missing-dotnet'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:MSBUILD_EXE_PATH = Join-Path $scratch 'missing-engine/MSBuild.dll'
    $env:MSBuildExtensionsPath = Join-Path $scratch 'missing-engine'
    $env:MSBuildSDKsPath = Join-Path $scratch 'missing-sdks'
    $env:GflatSdkRoot = $toolchain

    $app = Join-Path $scratch 'app'
    $library = Join-Path $scratch 'library'
    Invoke-Compiler new $library --name Library --library --no-stdlib
    Invoke-Compiler new $app --name App --no-stdlib
    $project = Join-Path $app 'App.gfproj'
    [IO.File]::WriteAllText((Join-Path $app 'main.gf'), 'int main() => Library::Answer();')
    [xml]$xml = Get-Content -LiteralPath $project
    $references = $xml.CreateElement('ItemGroup')
    $reference = $xml.CreateElement('ProjectReference')
    $reference.SetAttribute('Include', '../library/Library.gfproj')
    $references.AppendChild($reference) | Out-Null
    $xml.Project.AppendChild($references) | Out-Null
    $import = $xml.CreateElement('Import')
    $import.SetAttribute('Project', '../custom.targets')
    $xml.Project.AppendChild($import) | Out-Null
    $xml.Save($project)
    @'
<Project>
  <Target Name="RecordEngine" AfterTargets="Build">
    <WriteLinesToFile File="$(MSBuildProjectDirectory)/build-info.txt" Lines="$(Configuration);$(MSBuildBinPath)" Overwrite="true" />
  </Target>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $scratch 'custom.targets')

    Invoke-Compiler check $project
    # Both formats use the same private engine and build rules. SDK-style roots
    # and references must work even with an inherited, invalid SDK search path.
    foreach ($file in @($project, (Join-Path $library 'Library.gfproj'))) {
        [xml]$sdkProject = Get-Content -LiteralPath $file
        $sdkProject.Project.SetAttribute('Sdk', 'Gflat.Sdk')
        foreach ($rule in @($sdkProject.Project.Import)) {
            if ($rule.Project -like '*/build/Gflat.props' -or $rule.Project -like '*/build/Gflat.targets') { $sdkProject.Project.RemoveChild($rule) | Out-Null }
        }
        $sdkProject.Save($file)
    }
    Invoke-Compiler check $project
    Invoke-Compiler build $project --configuration Release --emit-ir
    $output = Join-Path $app 'bin/Release/App.ll'
    if (-not (Test-Path -LiteralPath $output)) { throw 'Bundled build produced no IR' }
    $info = Get-Content -LiteralPath (Join-Path $app 'build-info.txt')
    if ($info[0] -ne 'Release' -or [IO.Path]::GetFullPath($info[1]).TrimEnd('\', '/') -ne $compilerDirectory.TrimEnd('\', '/')) {
        throw "Build used the wrong configuration or engine: $info"
    }
    $timestamp = [IO.File]::GetLastWriteTimeUtc($output)
    Invoke-Compiler build $project --configuration Release --emit-ir
    if ([IO.File]::GetLastWriteTimeUtc($output) -ne $timestamp) { throw 'Unchanged bundled build did not skip compilation' }
    [IO.File]::WriteAllText((Join-Path $library 'main.gf'), 'namespace Library; public int Answer() => 7;')
    Invoke-Compiler build $project --configuration Release --emit-ir
    if (-not ([IO.File]::ReadAllText($output).Contains('ret i32 7'))) { throw 'Referenced source change was not compiled' }
    [IO.File]::WriteAllText((Join-Path $library 'main.gf'), 'namespace Library; public int Answer() => missing();')
    & $compiler check $project
    if ($LASTEXITCODE -eq 0) { throw 'Bundled check did not propagate a compiler failure' }
    Invoke-Compiler clean $project --configuration Release
    if (Test-Path -LiteralPath $output) { throw 'Bundled clean did not remove IR' }
    Write-Host 'Bundled MSBuild passed with dotnet unavailable: explicit imports, SDK-style projects, references, configurations, incremental builds, errors, and clean.'
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        # PowerShell converts $null to an empty string for a string parameter.
        # On .NET 9+ that sets an empty variable instead of removing it. An empty
        # MSBuildSDKsPath still overrides SDK discovery in a later dotnet build.
        if ($null -eq $savedEnvironment[$name]) {
            [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
        }
    }
    foreach ($name in $savedEnvironment.Keys) {
        if ($null -eq $savedEnvironment[$name]) {
            if (Test-Path -LiteralPath "Env:$name") { throw "Isolation test left an environment override: $name" }
        }
        elseif ([Environment]::GetEnvironmentVariable($name) -cne $savedEnvironment[$name]) {
            throw "Isolation test changed the original environment value: $name"
        }
    }
    if (-not $scratch.StartsWith($testBase, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup must remain inside its temporary directory' }
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
