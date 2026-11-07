param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'obj/gflat-language-configuration.json')
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../shared/gflat-language-configuration.json'
$configuration = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
# TextMate brace completion already inserts the closing-brace newline in
# PostReturn. Its indentation helper must not insert a second one.
foreach ($rule in $configuration.onEnterRules) {
    if ($rule.action.indent -eq 'indentOutdent') { $rule.action.indent = 'indent' }
}
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, ($configuration | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
