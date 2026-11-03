# Shared by the per-user Explorer integration scripts.
function Get-GflatExplorerRegistration([string]$VisualStudioPath, [string]$ClassesRoot, [switch]$AllowMissing) {
    $instance = Get-GflatVisualStudio $VisualStudioPath -AllowMissing:$AllowMissing
    $root = [IO.Path]::GetFullPath($instance.installationPath).Replace('/', '\').TrimEnd('\')
    $executable = Join-Path $root 'Common7/IDE/devenv.exe'
    if (-not $AllowMissing -and -not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Visual Studio was not found: $executable" }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $id = ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant())))).Replace('-', '').Substring(0, 16) }
    finally { $hash.Dispose() }
    $progId = "gflat.Project.$id"
    return [PSCustomObject]@{
        VisualStudioPath = $root
        Executable = $executable
        ProgId = $progId
        Key = Join-Path $ClassesRoot $progId
        Extension = Join-Path $ClassesRoot '.gfproj'
    }
}

function Assert-GflatExplorerOwner($Registration) {
    if (-not (Test-Path -LiteralPath $Registration.Key)) { return }
    $key = Get-Item -LiteralPath $Registration.Key
    if ($key.PSParentPath -ne (Get-Item -LiteralPath (Split-Path $Registration.Key)).PSPath) { throw 'Explorer registration escaped its registry root.' }
    if ($key.GetValue('GflatOwner') -ne 'gflat.VisualStudio.Explorer.v1' -or
        $key.GetValue('VisualStudioPath') -ne $Registration.VisualStudioPath) {
        throw "Explorer registration belongs to another owner: $($Registration.Key)"
    }
}

function Update-GflatExplorerAssociations {
    if (-not ('Gflat.ExplorerNotifications' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Gflat {
    public static class ExplorerNotifications {
        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
'@
    }
    [Gflat.ExplorerNotifications]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}
