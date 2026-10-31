<#
.SYNOPSIS  Downloads the real-world corpus listed in docs/performance/data/corpus-manifest.json (PDFs are never committed) and verifies size + SHA-256.
.EXAMPLE   .\tools\profile\Get-Corpus.ps1 -OutDir $env:USERPROFILE\pdf-corpus
Files whose hash differs are reported, not deleted: publishers revise documents, so a mismatch means timings are not comparable to the recorded run.
#>
param(
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $Manifest = (Join-Path $PSScriptRoot '..\..\docs\performance\data\corpus-manifest.json')
)
New-Item $OutDir -ItemType Directory -Force | Out-Null
foreach ($f in (Get-Content $Manifest -Raw | ConvertFrom-Json).files) {
    $dest = Join-Path $OutDir $f.file
    if (-not (Test-Path $dest)) { curl.exe -sL --max-time 600 -A 'Mozilla/5.0' -o $dest $f.url }
    $h = (Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $status = if ($h -eq $f.sha256) { 'OK' } else { "HASH MISMATCH (got $h)" }
    '{0,-28} {1,12:N0} bytes  {2}' -f $f.file, (Get-Item $dest).Length, $status
}
