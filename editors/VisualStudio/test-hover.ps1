#requires -PSEdition Desktop
param(
    [ValidateSet('Prepare', 'Validate')][string]$Mode,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$CatalogPath,
    [Parameter(Mandatory)][string]$CapturePath
)
$ErrorActionPreference = 'Stop'
# This test uses the installed IDE's real rich-content converter. The portable
# protocol tests run everywhere; this additional check needs a full VS install.
$ide = Get-ChildItem -LiteralPath (Join-Path $env:ProgramFiles 'Microsoft Visual Studio') -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object {
        $candidate = Join-Path $_.FullName 'Common7/IDE'
        if (Test-Path -LiteralPath (Join-Path $candidate 'CommonExtensions/Microsoft/LanguageServer/Microsoft.VisualStudio.LanguageServer.Protocol.Internal.dll')) { $candidate }
    }
} | Sort-Object { [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $_ 'devenv.exe')).FileMajorPart } -Descending | Select-Object -First 1
if (-not $ide) {
    if ($Mode -eq 'Prepare') { Write-Host 'VS rich-hover SDK check unavailable: full IDE not installed.'; return $false }
    throw 'Visual Studio IDE not found'
}
$folders = @(
    (Join-Path $ide 'CommonExtensions/Microsoft/LanguageServer'),
    (Join-Path $ide 'CommonExtensions/Microsoft/Editor'),
    (Join-Path $ide 'CommonExtensions/Microsoft/TextMate'),
    (Join-Path $ide 'PrivateAssemblies'),
    (Join-Path $ide 'PublicAssemblies'),
    $ide
)
$assemblies = @{}
$assemblies['netstandard'] = Join-Path $ide 'Remote Debugger/x64/netstandard.dll'
foreach ($folder in $folders) {
    foreach ($file in (Get-ChildItem -LiteralPath $folder -Filter '*.dll' -File)) {
        if (-not $assemblies.ContainsKey($file.BaseName)) { $assemblies[$file.BaseName] = $file.FullName }
    }
}
# Some VS releases keep Json.NET in versioned private assembly directories.
if (-not $assemblies.ContainsKey('Newtonsoft.Json')) {
    $json = Get-ChildItem -LiteralPath (Join-Path $ide 'PrivateAssemblies') -Directory -Filter 'Newtonsoft.Json.*' |
        Sort-Object { [version]$_.Name.Substring('Newtonsoft.Json.'.Length) } -Descending |
        ForEach-Object { Join-Path $_.FullName 'Newtonsoft.Json.dll' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $json) { throw 'Cannot locate the installed IDE Newtonsoft.Json assembly for the rich-hover test.' }
    $assemblies['Newtonsoft.Json'] = $json
}
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([Reflection.AssemblyName]$eventArgs.Name).Name
    if ($assemblies.ContainsKey($name)) { return [Reflection.Assembly]::LoadFrom($assemblies[$name]) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $client = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PackageDirectory 'gflat.VisualStudio.dll')))
    if ($client.GetType('gflat.VisualStudio.GflatSignatureHelpStyle')) { throw 'Signature appearance category override must be removed' }
    $themeType = $client.GetType('gflat.VisualStudio.GflatThemeColors', $true)
    $styleAttributes = @($themeType.GetCustomAttributes($false))
    if (-not ($styleAttributes | Where-Object { $_.GetType().Name -eq 'ContentTypeAttribute' -and $_.ContentTypes -contains 'gflat' })) { throw 'Signature style must be scoped to gflat' }
    if (-not ('gflat.PackageTests.ThemeColorCheck' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'test-theme.cs') -ReferencedAssemblies @(
            $assemblies['Microsoft.VisualStudio.Text.Logic'], $assemblies['Microsoft.VisualStudio.Text.UI.Wpf'],
            $assemblies['Microsoft.VisualStudio.Text.UI'], $assemblies['Microsoft.VisualStudio.Text.Data'],
            $assemblies['Microsoft.VisualStudio.CoreUtility'], 'PresentationCore', 'WindowsBase', 'System',
            $assemblies['netstandard']
        )
    }
    [gflat.PackageTests.ThemeColorCheck]::Run($themeType)
    $textMate = [Reflection.Assembly]::LoadFrom($assemblies['Microsoft.VisualStudio.TextMate.Core'])
    [gflat.PackageTests.ThemeColorCheck]::RunTextMateTheme($textMate, (Join-Path $PackageDirectory 'Grammars/gflat.tmLanguage.json'))
    Write-Host 'Installed Visual Studio TextMate loader selected the packaged theme and its native classification mappings.'
    Write-Host 'Roslyn theme synchronization copies actual foreground brushes and preserves tooltip font, size, background, and bold formatting.'
    # LSP content-type inheritance alone leaves shell preferences on Plain Text,
    # where automatic brace completion is disabled by default.
    $languageType = $client.GetType('gflat.VisualStudio.GflatLanguageInfo', $true)
    $packageType = $client.GetType('gflat.VisualStudio.GflatEditorPackage', $true)
    $registration = [IO.File]::ReadAllText((Join-Path $PackageDirectory 'gflat.pkgdef'))
    $languageId = $languageType.GUID.ToString('B').ToUpperInvariant()
    $packageId = $packageType.GUID.ToString('B').ToUpperInvariant()
    foreach ($entry in @(
        ('[$RootKey$\Languages\File Extensions\.gf]' + "`n" + '@="' + $languageId + '"'),
        ('[$RootKey$\Languages\Language Services\gflat]' + "`n" + '@="' + $languageId + '"'),
        ('[$RootKey$\Services\' + $languageId + ']' + "`n" + '@="' + $packageId + '"'),
        ('[$RootKey$\Packages\' + $packageId + ']'),
        '"ShowBraceCompletion"=dword:00000001'
    )) {
        if (-not $registration.Replace("`r`n", "`n").Contains($entry)) { throw "Missing gflat language registration: $entry" }
    }
    $language = [Activator]::CreateInstance($languageType)
    $arguments = [object[]]@($null)
    if ($languageType.GetMethod('GetLanguageName').Invoke($language, $arguments) -ne 0 -or $arguments[0] -ne 'gflat') { throw 'Shell language identity is incorrect' }
    if ($languageType.GetMethod('GetFileExtensions').Invoke($language, $arguments) -ne 0 -or $arguments[0] -ne '.gf') { throw 'Shell file extension is incorrect' }
    $icons = $client.GetType('gflat.VisualStudio.HoverIcons', $true).GetField('Catalog').GetValue($null)
    [Reflection.Assembly]::LoadFrom($assemblies['Newtonsoft.Json']) | Out-Null
    if ($Mode -eq 'Prepare') {
        [IO.File]::WriteAllText($CatalogPath, [Newtonsoft.Json.JsonConvert]::SerializeObject($icons))
        return $true
    }
    $protocol = [Reflection.Assembly]::LoadFrom($assemblies['Microsoft.VisualStudio.LanguageServer.Protocol.Internal'])
    $hoverType = $protocol.GetType('Microsoft.VisualStudio.LanguageServer.Protocol.VSInternalHover', $true)
    $hover = [Newtonsoft.Json.JsonConvert]::DeserializeObject([IO.File]::ReadAllText($CapturePath), $hoverType)
    $signatureType = $protocol.GetType('Microsoft.VisualStudio.LanguageServer.Protocol.VSInternalSignatureInformation', $true)
    $signature = [Newtonsoft.Json.JsonConvert]::DeserializeObject([IO.File]::ReadAllText($CapturePath + '.signature'), $signatureType)
    $signatureRuns = @($signature.ColorizedLabel.Runs)
    if (($signatureRuns.Text -join '') -ne $signature.Label) { throw 'Colored signature differs from its parameter label offsets' }
    foreach ($expected in @(@('Read', 'method name'), @('int', 'keyword'), @('count', 'parameter name'))) {
        if (-not ($signatureRuns | Where-Object { $_.Text -eq $expected[0] -and $_.ClassificationTypeName -eq $expected[1] })) { throw "Signature classification missing: $expected" }
    }
    Write-Host 'Colored signature help decoded successfully by the installed Visual Studio SDK.'
    if ($hover.RawContent.GetType().FullName -ne 'Microsoft.VisualStudio.Text.Adornments.ContainerElement') { throw 'Hover was not decoded as native VS content' }
    $header = @($hover.RawContent.Elements)[0]
    $elements = @($header.Elements)
    if ($elements[0].ImageId.Guid -ne $icons['method.public'].guid -or $elements[0].ImageId.Id -ne $icons['method.public'].id) { throw 'Hover did not use the VS method icon' }
    $runs = @($elements[1].Runs)
    if (-not ($runs | Where-Object { $_.Text -eq 'Read' -and $_.ClassificationTypeName -eq 'method name' })) { throw 'Method name was not classified' }
    if (-not ($runs | Where-Object { $_.Text -eq 'public' -and $_.ClassificationTypeName -eq 'keyword' })) { throw 'Method accessibility was not classified' }
    $exceptionHeader = @(@($hover.RawContent.Elements)[1].Runs)
    $exceptionRuns = @(@($hover.RawContent.Elements)[2].Runs)
    if (-not ($exceptionHeader | Where-Object { $_.Text -eq 'Known exceptions:' -and [int]$_.Style -eq 0 })) { throw 'Exception heading was not plain text' }
    if (-not ($exceptionRuns | Where-Object { $_.Text -eq 'Failure' -and $_.ClassificationTypeName -eq 'class name' })) { throw 'Exception type was not classified' }
    $wire = [Newtonsoft.Json.Linq.JObject]::Parse([IO.File]::ReadAllText($CapturePath))
    $script:navigationResult = $null
    $navigate = [Action[string,int,int]] { param($path, $line, $column); $script:navigationResult = @($path, $line, $column) }
    $render = $client.GetType('gflat.VisualStudio.HoverContent', $true).GetMethod('Create')
    $native = $render.Invoke($null, @($wire['_vs_rawContent'], $navigate))
    $link = @(@($native.Elements)[2].Runs) | Where-Object { $_.Text -eq 'Failure' }
    if (-not $link.NavigationAction) { throw 'Native exception type is not navigable' }
    $link.NavigationAction.Invoke()
    $target = @($wire['_vs_rawContent']['Elements'][2]['Runs']) | Where-Object { $_['Text'].Value -eq 'Failure' }
    $location = $target['_gflat_target']
    if ($script:navigationResult[0] -ne ([Uri]([string]$location['uri'])).LocalPath -or
        $script:navigationResult[1] -ne [int]$location['range']['start']['line'] -or
        $script:navigationResult[2] -ne [int]$location['range']['start']['character']) { throw 'Hover link did not navigate to the source declaration' }
    Write-Host 'Rich hover decoded successfully by the installed Visual Studio SDK, including catalog icon, classified runs, and native navigation callbacks.'
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }
