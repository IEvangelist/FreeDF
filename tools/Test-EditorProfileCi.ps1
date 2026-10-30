param(
    [Parameter(Mandatory)] [string] $Profile,
    [Parameter(Mandatory)] [string] $Output,
    [string] $Policy = (Join-Path $PSScriptRoot '..\docs\performance\editor-ci-known-misses.v1.json')
)
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'AdoBye.EditorSmoke\bin\Release\net10.0\AdoBye.EditorSmoke.dll'
New-Item $Output -ItemType Directory -Force | Out-Null
$cases = @{
    'new-timing' = 'CI timing regression: 03-typography-embedded.pdf cached-zoom'
    'known-timing' = 'CI timing regression: 01-spec-showcase.pdf open'
    'reopened' = 'NEW GOAL MISS | 03-typography-embedded.pdf / next-page / total-ms'
    'pixels' = 'Decoded pixel gate'
    'allocation' = 'Deterministic gate'
    'raster' = 'Deterministic gate'
    'resolved' = ''
    'missing' = 'Missing reference group'
}
foreach ($name in $cases.Keys) {
    $records = @(Get-Content $Profile -Raw | ConvertFrom-Json)
    foreach ($r in $records) {
        switch ($name) {
            'new-timing' { if ($r.document -eq '03-typography-embedded.pdf' -and $r.phase -eq 'cached-zoom') { $r.result.ms = 1000 } }
            'known-timing' { if ($r.document -eq '01-spec-showcase.pdf' -and $r.phase -eq 'open') { $r.result.ms = 2000 } }
            'pixels' { if ($r.phase -eq 'first-page-image') { $r.pixelHash = 'BAD' } }
            'allocation' {
                if ($r.phase -eq 'fresh-render-input') {
                    foreach ($stage in $r.result.workerStages) {
                        if ($stage.engine) { $stage.engine.allocatedBytes = 300000000 }
                    }
                }
            }
            'raster' {
                if ($r.phase -eq 'cached-zoom') {
                    $r.result.workerStages = @([pscustomobject]@{
                        url = '/api/page/0.png?dpi=41'
                        engine = [pscustomobject]@{ rasterMs = 0; pngEncodeMs = 0; allocatedBytes = 0; cacheHit = $false }
                    })
                }
            }
            'reopened' { if ($r.document -eq '03-typography-embedded.pdf' -and $r.phase -eq 'next-page') { $r.result.ms = 110 } }
            'resolved' { if ($r.document -eq '01-spec-showcase.pdf' -and $r.phase -eq 'open') { $r.result.ms = 200 } }
        }
    }
    if ($name -eq 'missing') { $records = @($records | Where-Object { !($_.document -eq '03-typography-embedded.pdf' -and $_.phase -eq 'cached-zoom') }) }
    $path = Join-Path $Output "$name.json"
    $records | ConvertTo-Json -Depth 100 | Set-Content $path
    & dotnet $runner --profile-ci $path $Policy *> "$path.log"
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
