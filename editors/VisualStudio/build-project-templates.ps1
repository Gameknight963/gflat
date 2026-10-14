param([Parameter(Mandatory)][string]$Compiler)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = Join-Path $PSScriptRoot 'obj/projects'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$staging = Join-Path $output ('staging-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($kind in @('App', 'Library')) {
        $directory = Join-Path $staging $kind
        $arguments = @('new', $directory, '--name', 'Template')
        if ($kind -eq 'Library') { $arguments += '--library' }
        & $Compiler @arguments | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Cannot generate $kind template through gflat new" }
        $projectPath = Join-Path $directory 'Template.gfproj'
        [xml]$project = Get-Content -LiteralPath $projectPath
        # CPS supplies the default SDK before initial project evaluation. Keep
        # explicit SDK overrides, with no build-machine path in the template.
        foreach ($property in @($project.SelectNodes('/Project/PropertyGroup/GflatSdkRoot'))) {
            $property.ParentNode.RemoveChild($property) | Out-Null
        }
        $property = $project.CreateElement('GflatSdkRoot')
        $property.SetAttribute('Condition', "'`$(GflatSdkRoot)' == ''")
        $property.InnerText = '$(GflatDefaultSdkRoot)'
        $project.SelectSingleNode('/Project/PropertyGroup').AppendChild($property) | Out-Null
        $project.Save($projectPath)
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Templates/Projects/$kind.vstemplate") -Destination (Join-Path $directory 'Project.vstemplate')
        $archive = Join-Path $output "gflat-$($kind.ToLowerInvariant()).zip"
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
        [IO.Compression.ZipFile]::CreateFromDirectory($directory, $archive)
    }
}
finally {
    $resolved = [IO.Path]::GetFullPath($staging)
    $allowed = [IO.Path]::GetFullPath($output) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe template cleanup path' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
