param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'obj/gflat-language-configuration.json')
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../shared/gflat-language-configuration.json'
$configuration = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
# Native brace completion owns the extra newline and caret placement. An Enter
# override runs first, on the closing-brace line, and overrides its alignment.
$configuration.onEnterRules = @($configuration.onEnterRules | Where-Object {
    $_.action.indent -ne 'indentOutdent'
})
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, ($configuration | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
