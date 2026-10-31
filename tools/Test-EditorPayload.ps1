param(
    [Parameter(Mandatory)] [string] $Site,
    [string] $Budget = (Join-Path $PSScriptRoot '..\docs\performance\editor-startup-budget.v1.json'),
    [switch] $Aot,
    [string] $Report
)
# Deterministic startup-payload gate: asset count and bytes of the published site against checked-in ceilings.
$ErrorActionPreference = 'Stop'
$limits = (Get-Content $Budget -Raw | ConvertFrom-Json).$(if ($Aot) { 'aot' } else { 'nonAot' })
$root = (Resolve-Path $Site).Path
$files = @(Get-ChildItem $root -Recurse -File | Where-Object { $_.Name -ne '.nojekyll' })
$gzip = 0L
foreach ($file in $files) {
    $stream = New-Object IO.MemoryStream
    $zip = New-Object IO.Compression.GZipStream($stream, [IO.Compression.CompressionLevel]::Optimal, $true)
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $zip.Write($bytes, 0, $bytes.Length); $zip.Dispose()
    $gzip += $stream.Length
}
$raw = ($files | Measure-Object Length -Sum).Sum
$largest = $files | Sort-Object Length -Descending | Select-Object -First 1
$failures = @()
if ($files.Count -gt $limits.maxFiles) { $failures += "files $($files.Count) > $($limits.maxFiles)" }
if ($raw -gt $limits.maxRawBytes) { $failures += "raw bytes $raw > $($limits.maxRawBytes)" }
if ($gzip -gt $limits.maxGzipBytes) { $failures += "gzip bytes $gzip > $($limits.maxGzipBytes)" }
if ($largest.Length -gt $limits.maxLargestFileBytes) { $failures += "largest file $($largest.Name) $($largest.Length) > $($limits.maxLargestFileBytes)" }
$sidecars = @($files | Where-Object { $_.Extension -in '.br', '.gz' })
if ($sidecars.Count) { $failures += "$($sidecars.Count) precompressed sidecars published (Pages negotiates gzip itself)" }
$summary = [ordered]@{ aot = [bool]$Aot; files = $files.Count; rawBytes = $raw; gzipBytes = $gzip; largestFile = $largest.Name; largestBytes = $largest.Length; failures = $failures }
if ($Report) { $summary | ConvertTo-Json | Set-Content $Report }
Write-Host ("Startup payload: {0} files, {1:N2} MB raw, {2:N2} MB gzip, largest {3} ({4:N2} MB)" -f $files.Count, ($raw / 1MB), ($gzip / 1MB), $largest.Name, ($largest.Length / 1MB))
if ($failures.Count) { throw "Startup payload budget exceeded: $($failures -join '; ')" }
