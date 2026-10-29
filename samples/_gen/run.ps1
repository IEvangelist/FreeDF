param([string]$Only = "", [string]$Sheet = "")
# Rebuild (optionally filtered), validate with our renderer, and make a contact sheet.
$root = Split-Path (Split-Path $PSScriptRoot)
Set-Location $root
python samples\_gen\build_all.py $Only
if (-not $Only -or "20-report".Contains($Only)) {
    dotnet tools\Pdf.Samples\bin\Release\net10.0\Pdf.Samples.dll repack samples\20-report.pdf samples\26-repacked-object-streams.pdf
}
dotnet tools\Pdf.Samples\bin\Release\net10.0\Pdf.Samples.dll validate samples --out samples\_gen\out
if ($Sheet) { python samples\_gen\sheet.py $Sheet }
