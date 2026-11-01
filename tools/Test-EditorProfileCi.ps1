param(
    [Parameter(Mandatory)] [string] $Profile,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Policy = (Join-Path $PSScriptRoot '..\docs\performance\editor-ci-known-misses.v1.json')
)
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'AdoBye.EditorSmoke\bin\Release\net10.0\AdoBye.EditorSmoke.dll'
New-Item $Output -ItemType Directory -Force | Out-Null
# Synthetic controls must be independent of real timing regressions in the input run.
# Only timings are normalized; missing groups, allocations and pixel evidence remain intact.
$control = @(Get-Content $Profile -Raw | ConvertFrom-Json)
foreach ($r in $control) {
    if ($r.PSObject.Properties['inputAckMs'] -and $null -ne $r.inputAckMs) { $r.inputAckMs = 1 }
    if (!$r.PSObject.Properties['result']) { continue }
    $result = $r.result
    if ($result.PSObject.Properties['ms']) { $result.ms = 1 }
    foreach ($field in @('interactions', 'presentation', 'longTasks')) {
        if ($result.PSObject.Properties[$field]) { foreach ($entry in $result.$field) { $entry.ms = 1 } }
    }
    if ($result.PSObject.Properties['heartbeatDelays']) { $result.heartbeatDelays = @($result.heartbeatDelays | ForEach-Object { 1 }) }
}
$controlPath = Join-Path $Output 'control.json'
$control | ConvertTo-Json -Depth 100 | Set-Content $controlPath
& dotnet $runner --profile-ci $controlPath $Policy *> "$controlPath.log"
if ($LASTEXITCODE -ne 0) { throw "Synthetic control failed before fault injection. See $controlPath.log" }
$cases = @{
    'new-timing' = 'CI timing regression: 03-typography-embedded.pdf cached-zoom'
    'known-timing' = 'CI timing regression: 01-spec-showcase.pdf open'
    'reopened' = 'NEW GOAL MISS | 03-typography-embedded.pdf / next-page / total-ms'
    'pixels' = 'Decoded pixel gate'
    'allocation' = 'Deterministic gate'
    'raster' = 'Deterministic gate'
    'resolved' = ''
    'missing' = 'Missing reference group'
    'single-outlier' = ''
    'two-outliers' = 'CI timing regression: 09-images.pdf save-download event-frame-max-ms'
}
foreach ($name in $cases.Keys) {
    $records = @(Get-Content $controlPath -Raw | ConvertFrom-Json)
    foreach ($r in $records) {
        switch ($name) {
            'new-timing' { if ($r.document -eq '03-typography-embedded.pdf' -and $r.phase -eq 'cached-zoom') { $r.result.ms = 1000 } }
            'known-timing' { if ($r.document -eq '01-spec-showcase.pdf' -and $r.phase -eq 'open') { $r.result.ms = 2000 } }
            'pixels' { if ($r.phase -eq 'first-page-image' -and $r.repeat -eq 0) { $r.pixelHash = 'BAD' } }
            'allocation' {
                if ($r.phase -eq 'fresh-render-input' -and $r.repeat -eq 0) {
                    foreach ($stage in $r.result.workerStages) {
                        if ($stage.engine) { $stage.engine.allocatedBytes = 300000000 }
                    }
                }
            }
            'raster' {
                if ($r.phase -eq 'cached-zoom' -and $r.repeat -eq 0) {
                    $r.result.workerStages = @([pscustomobject]@{
                        url = '/api/page/0.png?dpi=41'
                        engine = [pscustomobject]@{ rasterMs = 0; pngEncodeMs = 0; allocatedBytes = 0; cacheHit = $false }
                    })
                }
            }
            'reopened' { if ($r.document -eq '03-typography-embedded.pdf' -and $r.phase -eq 'next-page') { $r.result.ms = 110 } }
            'resolved' { if ($r.document -eq '01-spec-showcase.pdf' -and $r.phase -eq 'open') { $r.result.ms = 200 } }
            { $_ -in 'single-outlier', 'two-outliers' } {
                if ($r.document -eq '09-images.pdf' -and $r.phase -eq 'save-download') {
                    $slow = $r.repeat -eq 0 -or ($name -eq 'two-outliers' -and $r.repeat -eq 1)
                    foreach ($event in $r.result.presentation) { $event.ms = $(if ($slow) { 85 } else { 10 }) }
                }
            }
        }
    }
    if ($name -eq 'missing') { $records = @($records | Where-Object { !($_.document -eq '03-typography-embedded.pdf' -and $_.phase -eq 'cached-zoom') }) }
    $path = Join-Path $Output "$name.json"
    $records | ConvertTo-Json -Depth 100 | Set-Content $path
    & dotnet $runner --profile-ci $path $Policy *> "$path.log"
    if ($name -eq 'single-outlier') {
        if ($LASTEXITCODE -ne 0) { throw 'A single-repeat timing outlier unexpectedly failed hosted CI' }
        $result = Get-Content "$path.ci.json" -Raw | ConvertFrom-Json
        if (!(($result.warnings -join "`n").Contains('09-images.pdf save-download event-frame-max-ms'))) { throw 'Single-repeat outlier warning missing' }
        $summary = Get-Content "$path.ci.md" -Raw
        if (!$summary.Contains('85.0')) { throw 'Outlier maximum was hidden from the job summary' }
        Write-Host 'PASS: single-repeat outlier passes with a visible warning and maximum'
        continue
    }
    if ($name -eq 'resolved') {
        if ($LASTEXITCODE -ne 0) { throw 'An improved known goal unexpectedly failed' }
        Write-Host 'PASS: improved known goal accepted without changing regression ceilings'
        continue
    }
    if ($name -eq 'reopened') {
        if ($LASTEXITCODE -ne 0) { throw 'A removed known miss unexpectedly failed the relative timing gate' }
        $summary = Get-Content "$path.ci.md" -Raw
        if (!$summary.Contains($cases[$name])) { throw 'A reopened strict-goal miss was not shown as NEW GOAL MISS' }
        Write-Host 'PASS: a resolved goal regression is surfaced as a new strict-goal miss'
        continue
    }
    if ($LASTEXITCODE -eq 0) { throw "$name unexpectedly passed" }
    $result = Get-Content "$path.ci.json" -Raw | ConvertFrom-Json
    if (!(($result.failures -join "`n").Contains($cases[$name]))) { throw "$name failed for the wrong reason" }
    Write-Host "PASS: $name rejected for expected reason"
}
$global:LASTEXITCODE = 0
