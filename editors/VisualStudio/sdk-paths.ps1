# Shared by the Windows Visual Studio SDK setup scripts, not the compiler.
function Get-GflatVisualStudio([string]$VisualStudioPath) {
    if ($env:OS -ne 'Windows_NT') { throw 'These scripts require Windows Visual Studio.' }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $instances = @()
    if (Test-Path -LiteralPath $vswhere) {
        $instances = & $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.CoreEditor -format json | ConvertFrom-Json
    }
    if ($VisualStudioPath) {
        $path = [IO.Path]::GetFullPath($VisualStudioPath).TrimEnd('\', '/')
        $instance = $instances | Where-Object { $_.installationPath.TrimEnd('\', '/') -eq $path } | Select-Object -First 1
        if (-not $instance) { $instance = [PSCustomObject]@{ installationPath = $path; instanceId = $null; installationVersion = $null } }
    } else {
        $instance = $instances | Sort-Object { [version]$_.installationVersion } -Descending | Select-Object -First 1
        if (-not $instance) { throw 'Visual Studio was not found. Pass -VisualStudioPath with its installation directory.' }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $instance.installationPath 'MSBuild/Current/Bin/MSBuild.exe'))) {
        throw "Visual Studio MSBuild was not found under $($instance.installationPath)."
    }
    return $instance
}

function Get-GflatSdkTarget([string]$VisualStudioPath) {
    $root = [IO.Path]::GetFullPath($VisualStudioPath).TrimEnd('\', '/')
    $target = [IO.Path]::GetFullPath((Join-Path $root 'MSBuild/Sdks/Gflat.Sdk'))
    if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'SDK path escaped the Visual Studio installation.' }
    # Do not follow junctions when installing or removing files in Program Files.
    for ($directory = [IO.DirectoryInfo]$target; $directory -and $directory.FullName.Length -ge $root.Length; $directory = $directory.Parent) {
        if ($directory.Exists -and ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "SDK setup does not follow directory links: $($directory.FullName)" }
    }
    $sdk = Join-Path $target 'Sdk'
    if ((Test-Path -LiteralPath $sdk) -and ((Get-Item -LiteralPath $sdk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "SDK setup does not follow directory links: $sdk" }
    return $target
}

function Assert-GflatSdkOwner([string]$Target) {
    $marker = Join-Path $Target '.gflat-sdk-install.json'
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { throw "Refusing to modify an SDK directory not created by these scripts: $Target" }
    $record = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
    if ($record.owner -ne 'gflat.VisualStudio.SDK.v1') { throw "Unrecognized SDK ownership marker: $marker" }
}

function Find-GflatExtensionSdk($Instance) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Sdk/build/Gflat.props')) { return (Join-Path $PSScriptRoot 'Sdk') }
    $roots = @((Join-Path $Instance.installationPath 'Common7/IDE/Extensions'))
    if ($Instance.instanceId) {
        $major = ([version]$Instance.installationVersion).Major
        $roots += Join-Path $env:LOCALAPPDATA "Microsoft/VisualStudio/$major.0_$($Instance.instanceId)/Extensions"
    }
    $extensions = foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($manifest in Get-ChildItem -LiteralPath $root -Filter extension.vsixmanifest -File -Recurse) {
            [xml]$xml = Get-Content -LiteralPath $manifest.FullName
            if ($xml.PackageManifest.Metadata.Identity.Id -eq 'gflat.LanguageSupport.72c57259-8ade-48ad-9c74-1ec89cb65515') {
                [PSCustomObject]@{ Version = [version]$xml.PackageManifest.Metadata.Identity.Version; Sdk = (Join-Path $manifest.DirectoryName 'Sdk') }
            }
        }
    }
    $extension = $extensions | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $extension) { throw 'Install the gflat VSIX first, or pass -SdkRoot pointing to its Sdk directory.' }
    return $extension.Sdk
}

function ConvertTo-GflatImportPath([string]$Path) {
    # MSBuild escaping happens before XML escaping. Preserve literal $, %, etc.
    $escaped = [Text.StringBuilder]::new()
    foreach ($character in $Path.ToCharArray()) {
        if ('%$@;?*''()'.IndexOf($character) -ge 0) { [void]$escaped.Append('%').Append(([int]$character).ToString('X2')) }
        else { [void]$escaped.Append($character) }
    }
    return [Security.SecurityElement]::Escape($escaped.ToString())
}
