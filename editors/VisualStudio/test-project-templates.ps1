param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $PackageDirectory 'Compiler/gflat.exe'
$sdk = Join-Path $PackageDirectory 'Sdk'
if (Test-Path -LiteralPath (Join-Path $sdk 'build/Sdks/Gflat.Sdk')) { throw 'VSIX must not ship MSBuild SDK registration files' }
foreach ($file in @('build/Gflat.props', 'build/Gflat.targets', 'build/Rules/General.xaml', 'Standard/Standard.gfproj', 'Standard/core/Text/String.gf', 'Standard/libc/GC.gf')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sdk $file))) { throw "Bundled SDK is missing $file" }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'extension.vsixmanifest')
if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'Microsoft.VisualStudio.ProjectTemplate' -and $_.Path -eq 'ProjectTemplates' })) {
    throw 'VSIX must register its native project templates'
}
if (-not ($manifest.PackageManifest.Assets.Asset | Where-Object { $_.Type -eq 'Microsoft.VisualStudio.Assembly' -and $_.Path -eq 'gflat.VisualStudio.dll' })) {
    throw 'VSIX must register the item-template wizard assembly for template loading'
}
[xml]$itemTemplate = Get-Content -Raw -LiteralPath (Join-Path $PackageDirectory 'ItemTemplates/gflat-source/SourceFile.vstemplate')
$identity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $PackageDirectory 'gflat.VisualStudio.dll'))
if ($itemTemplate.VSTemplate.WizardExtension.Assembly -ne $identity.FullName -or
    $itemTemplate.VSTemplate.WizardExtension.FullClassName -ne 'gflat.VisualStudio.GflatSourceFileWizard') {
    throw 'Source file template must reference the packaged wizard assembly identity'
}
if ($identity.GetPublicKeyToken().Length -eq 0) { throw 'Template wizard assembly must have a strong name' }
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
    if ($projectDefinition.Project.Sdk -ne 'Gflat.Sdk') { throw 'Project templates must use Gflat.Sdk' }
    if ($projectDefinition.Project.Import -or $projectDefinition.Project.PropertyGroup.GflatSdkRoot) {
        throw 'Project templates must resolve their toolchain through the SDK'
    }
    $projectText.Replace('$projectname$', $name).Replace('$safeprojectname$', $safeName) | Set-Content -LiteralPath $project -Encoding utf8
    foreach ($item in $projectNode.ProjectItem) {
        $filename = $item.InnerText
        $destination = if ($item.TargetFileName) { $item.TargetFileName } else { $filename }
        $text = Get-Content -Raw -LiteralPath (Join-Path $template $filename)
        if ($item.ReplaceParameters -eq 'true') { $text = $text.Replace('$projectname$', $name).Replace('$safeprojectname$', $safeName) }
        $text | Set-Content -LiteralPath (Join-Path $directory $destination) -Encoding utf8
    }
    # Use the packaged resolver entry without registering an SDK on this machine.
    $previousSdkPath = $env:MSBuildSDKsPath
    try {
        $env:MSBuildSDKsPath = Join-Path $PackageDirectory 'Compiler/Sdks'
        dotnet msbuild $project -t:Check "-p:GflatDefaultSdkRoot=$sdk" "-p:GflatBundledCompiler=$compiler" -nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "SDK-style $kind template evaluation/check failed" }
    }
    finally { $env:MSBuildSDKsPath = $previousSdkPath }
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
$relativeDirectory = Join-Path $PackageDirectory 'relative-sdk'
New-Item -ItemType Directory -Path $relativeDirectory | Out-Null
$relativeProject = Join-Path $relativeDirectory 'relative.gfproj'
@'
<Project DefaultTargets="Build">
  <PropertyGroup><GflatSdkRoot>../Sdk</GflatSdkRoot></PropertyGroup>
  <Import Project="$(GflatSdkRoot)/build/Gflat.props" />
  <Import Project="$(GflatSdkRoot)/build/Gflat.targets" />
</Project>
'@ | Set-Content -LiteralPath $relativeProject -Encoding utf8
[IO.File]::WriteAllText((Join-Path $relativeDirectory 'Main.gf'), 'int main() => 0;')
dotnet msbuild $relativeProject -t:Build -p:BuildingInsideVisualStudio=true -p:GflatEmitIR=true "-p:GflatCompilerPath=$compiler" -nologo -v:minimal
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $relativeDirectory 'obj/Debug/relative.up-to-date.xml'))) {
    throw 'The relative SDK project must record its successful IDE build with a Standard reference'
}
Write-Host 'Native project template discovery, bundled SDK evaluation, and generated App/Library projects passed.'
