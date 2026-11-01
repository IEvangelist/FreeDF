# Independent validation of the samples: regenerates the full collection, then checks all 27 files with
# pikepdf (qpdf) and pdfium (Python), and with this repo's own parser/renderer. Exits non-zero on any failure.
#   pip install pikepdf pypdfium2 numpy
param([string]$Samples = (Join-Path $PSScriptRoot '..\samples'))
$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot 'FreeDF.Samples'
dotnet build $tool -c Release --nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$dll = Join-Path $tool 'bin\Release\net10.0\FreeDF.Samples.dll'
dotnet $dll generate $Samples 27-            # the merged file is not committed; build it from the committed 01-26
if ($LASTEXITCODE) { exit $LASTEXITCODE }
python (Join-Path $PSScriptRoot 'validate_samples.py') check $Samples
$py = $LASTEXITCODE
dotnet $dll validate $Samples
exit ($py -bor $LASTEXITCODE)
