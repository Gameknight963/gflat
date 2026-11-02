param([string]$PlanDirectory = 'artifacts/test-plan-check')
$ErrorActionPreference = 'Stop'
function List-Cases([string]$settings, [string]$name) {
    $arguments = @('test', 'gflat.Tests/gflat.Tests.csproj', '--no-build', '--no-restore', '--list-tests')
    if ($settings) { $arguments += @('--settings', $settings) }
    $listing = @(& dotnet @arguments)
    if ($LASTEXITCODE -ne 0) { throw "Discovery failed: $name" }
    $listing | Set-Content "$PlanDirectory/$name.txt"
    $active = $false
    foreach ($line in $listing) {
        if ($line -match 'The following Tests are available:') { $active = $true; continue }
        if ($active -and $line.StartsWith('    ')) { $line.Trim() }
    }
}
dotnet run --project build/TestPlan -- plan gflat.Tests/bin/Debug/net10.0/gflat.Tests.dll $PlanDirectory 4
if ($LASTEXITCODE -ne 0) { throw 'Test planning failed' }
$all = @(List-Cases '' 'all')
$primary = @()
$fallbackCount = 0
for ($shard = 0; $shard -lt 4; $shard++) {
    $cases = @(List-Cases "$PlanDirectory/$shard-primary.runsettings" "primary-$shard")
    $primary += $cases
    $fallback = @(List-Cases "$PlanDirectory/$shard-fallback.runsettings" "fallback-$shard")
    $fallbackCount += $fallback.Count
    Write-Output "Shard ${shard}: $($cases.Count) primary, $($fallback.Count) fallback cases"
}
$expected = @($all | Group-Object | ForEach-Object { "$($_.Count):$($_.Name)" } | Sort-Object)
$actual = @($primary | Group-Object | ForEach-Object { "$($_.Count):$($_.Name)" } | Sort-Object)
$difference = @(Compare-Object $expected $actual)
if ($all.Count -eq 0 -or $difference.Count -ne 0) { throw "Shard coverage mismatch: $($difference | Out-String)" }
Write-Output "All $($all.Count) discovered cases occur exactly once in primary shards. Fallback: $fallbackCount cases, versus $($all.Count) in the old second pass."

# Exercise observation-based fallback expansion without executing the test body.
$manifest = Get-Content "$PlanDirectory/manifest.json" -Raw | ConvertFrom-Json
$selected = $null
foreach ($part in $manifest) {
    foreach ($group in $part.classes) {
        foreach ($method in $group.methods) {
            if (-not $method.fallback -and -not $selected) {
                $selected = @{ Shard = $part.shard; Name = $group.name + '.' + $method.name }
            }
        }
    }
}
if (-not $selected) { throw 'No explicit optimization method available for fallback verification' }
$settingsPath = "$PlanDirectory/$($selected.Shard)-fallback.runsettings"
$original = [IO.File]::ReadAllText([IO.Path]::GetFullPath($settingsPath))
$profiles = "$PlanDirectory/profiles/$($selected.Shard)-primary"
New-Item -ItemType Directory -Force $profiles | Out-Null
$trace = Join-Path $profiles ('observation-' + [Guid]::NewGuid().ToString('N') + '.jsonl')
try {
    @{ stage = 'native.compile-link'; detail = 'O0, default'; test = $selected.Name } |
        ConvertTo-Json -Compress | Set-Content $trace
    dotnet run --project build/TestPlan --no-build -- fallback $PlanDirectory $selected.Shard
    if ($LASTEXITCODE -ne 0) { throw 'Fallback expansion failed' }
    [xml]$expanded = Get-Content $settingsPath
    $filter = $expanded.RunSettings.RunConfiguration.TestCaseFilter
    if (-not $filter.Contains('FullyQualifiedName=' + $selected.Name)) { throw 'Observed method was not added' }
    dotnet run --project build/TestPlan --no-build -- fallback $PlanDirectory $selected.Shard
    if ($LASTEXITCODE -ne 0) { throw 'Repeated fallback expansion failed' }
    [xml]$repeated = Get-Content $settingsPath
    if ($filter -ne $repeated.RunSettings.RunConfiguration.TestCaseFilter) { throw 'Fallback expansion must be idempotent' }
    Write-Output 'Observed default compilations expand the fallback exactly once.'
}
finally {
    Remove-Item -LiteralPath $trace
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($settingsPath), $original)
}
