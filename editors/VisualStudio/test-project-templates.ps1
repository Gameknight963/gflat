param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $PackageDirectory 'Compiler/gflat.exe'
$sdk = Join-Path $PackageDirectory 'Sdk'
if (Test-Path -LiteralPath (Join-Path $sdk 'build/Sdks/Gflat.Sdk')) { throw 'VSIX must not ship MSBuild SDK registration files' }
foreach ($file in @('build/Gflat.props', 'build/Gflat.targets', 'build/Rules/General.xaml', 'std/std.gfproj', 'std/core/String.gf', 'std/libc/GC.gf')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sdk $file))) { throw "Bundled SDK is missing $file" }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'extension.vsixmanifest')
if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'Microsoft.VisualStudio.ProjectTemplate' -and $_.Path -eq 'ProjectTemplates' })) {
    throw 'VSIX must register its native project templates'
}
$templateManifests = @(Get-ChildItem -LiteralPath (Join-Path $PackageDirectory 'ProjectTemplates') -Filter '*.vstman')
if ($templateManifests.Count -eq 0) { throw 'VSSDK did not generate a project template discovery manifest' }
$catalog = ($templateManifests | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
foreach ($kind in @('app', 'library')) {
    $template = Join-Path $PackageDirectory "ProjectTemplates/gflat-$kind"
    [xml]$definition = Get-Content -LiteralPath (Join-Path $template 'Project.vstemplate')
    if ($definition.VSTemplate.Type -ne 'Project' -or $definition.VSTemplate.TemplateData.ProjectType -ne 'gflat') {
        throw 'Template must instantiate a real gflat project'
    }
    $identifier = $definition.VSTemplate.TemplateData.TemplateID
    if (-not $catalog.Contains($identifier)) { throw "Project template $identifier is absent from the discovery catalog" }
    $directory = Join-Path $PackageDirectory "generated-$kind"
    New-Item -ItemType Directory -Path $directory | Out-Null
    # A project filename may be a language keyword; it must not become an
    # invalid namespace through Visual Studio's C#-oriented safe-name token.
    $name = if ($kind -eq 'app') { 'Application with spaces' } else { 'class' }
    $safeName = $name.Replace(' ', '_')
    $projectNode = $definition.VSTemplate.TemplateContent.Project
    $target = $projectNode.TargetFileName.Replace('$projectname$', $name)
    $project = Join-Path $directory $target
    $projectText = Get-Content -Raw -LiteralPath (Join-Path $template $projectNode.File)
    if ($projectText -match '(?i)[a-z]:[/\\]|\.\.[/\\]') { throw 'Project template contains a build-machine or relative SDK installation path' }
    [xml]$projectDefinition = $projectText
    if ($projectDefinition.Project.Sdk) { throw 'Project templates must not require an installed MSBuild SDK' }
    $imports = @($projectDefinition.Project.Import | ForEach-Object { $_.Project })
    if ($imports -notcontains '$(GflatSdkRoot)/build/Gflat.props' -or $imports -notcontains '$(GflatSdkRoot)/build/Gflat.targets') {
        throw 'Project templates must import the bundled build rules explicitly'
    }
    $projectText.Replace('$projectname$', $name).Replace('$safeprojectname$', $safeName) | Set-Content -LiteralPath $project -Encoding utf8
    foreach ($item in $projectNode.ProjectItem) {
        $filename = $item.InnerText
        $destination = if ($item.TargetFileName) { $item.TargetFileName } else { $filename }
        $text = Get-Content -Raw -LiteralPath (Join-Path $template $filename)
        if ($item.ReplaceParameters -eq 'true') { $text = $text.Replace('$projectname$', $name).Replace('$safeprojectname$', $safeName) }
        $text | Set-Content -LiteralPath (Join-Path $directory $destination) -Encoding utf8
    }
    # Simulate CPS's initial-evaluation defaults using the actual packaged SDK.
    dotnet msbuild $project -t:Check "-p:GflatSdkRoot=$sdk" "-p:GflatBundledCompiler=$compiler" -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "CPS-style $kind template evaluation/check failed" }
    # The standalone bundled compiler must discover the same SDK without VS.
    if ($kind -eq 'app') {
        $result = & $compiler build $project --run
        if ($LASTEXITCODE -ne 0 -or $result -notcontains 'Hello from gflat!') { throw 'Packaged application template failed to build and run' }
    }
    else {
        & $compiler check $project
        if ($LASTEXITCODE -ne 0) { throw 'Packaged library template failed to check outside Visual Studio' }
    }
}
Write-Host 'Native project template discovery, bundled SDK evaluation, and generated App/Library projects passed.'
