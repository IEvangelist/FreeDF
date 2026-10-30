param(
    [string] $Output = (Join-Path $PSScriptRoot 'AdoBye.EditorWasm\bin\pages'),
    [string] $BasePath = '/csharp-pdf/',
    [switch] $Aot,
    [switch] $SkipSamples,
    [string] $ArtifactsPath
)
$ErrorActionPreference = 'Stop'
if ($BasePath -notmatch '^/([A-Za-z0-9._-]+/)*$') { throw 'BasePath must start and end with / and contain only path segments.' }
$project = Join-Path $PSScriptRoot 'AdoBye.EditorWasm\AdoBye.EditorWasm.csproj'
$publishArgs = @('publish', $project, '-c', 'Release', '-o', $Output, '--nologo')
if ($Aot) { $publishArgs += @('-p:RunAOTCompilation=true', '-p:PublishTrimmed=true', '-p:WasmEnableSIMD=true') }
if ($ArtifactsPath) { $publishArgs += @('--artifacts-path', $ArtifactsPath) }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'WebAssembly publish failed.' }
$site = Join-Path $Output 'wwwroot'
$index = Join-Path $site 'index.html'
$html = [IO.File]::ReadAllText($index).Replace('<head>', "<head>`n<base href=`"$BasePath`">`n<meta name=`"editor-host`" content=`"wasm`">")
[IO.File]::WriteAllText($index, $html)
[IO.File]::WriteAllText((Join-Path $site '.nojekyll'), '')
foreach ($asset in @('editor.css', 'editor.js', 'host.js', 'worker.js', 'pool.js', 'scheduler.js', 'replica-worker.js', 'recovery.js', 'samples.js', 'bitstream-vera-license.txt', '_framework\dotnet.js')) {
    if (!(Test-Path (Join-Path $site $asset))) { throw "Missing site asset: $asset" }
}
# Lazily fetched sample PDFs (never part of the startup payload): 01-26 are committed, 27 is generated deterministically in a scratch folder.
if (!$SkipSamples) {
    $sampleSource = Join-Path $PSScriptRoot '..\samples'
    $work = Join-Path ([IO.Path]::GetTempPath()) ('pages-samples-' + [Guid]::NewGuid().ToString('N'))
    $tool = Join-Path $work 'tool'
    $inputDir = Join-Path $work 'input'
    $samplesProject = Join-Path $PSScriptRoot 'AdoBye.Samples\AdoBye.Samples.csproj'
    try {
        New-Item -ItemType Directory -Path $inputDir | Out-Null
        dotnet build $samplesProject -c Release -o $tool --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'Building the sample generator failed.' }
        Get-ChildItem $sampleSource -Filter '*.pdf' | Where-Object { $_.Name -match '^\d\d-' -and $_.Name -notmatch '^27-' } | Copy-Item -Destination $inputDir
        dotnet (Join-Path $tool 'AdoBye.Samples.dll') generate $inputDir 27-
        if ($LASTEXITCODE -ne 0) { throw 'Generating 27-full-collection.pdf failed.' }
        dotnet (Join-Path $tool 'AdoBye.Samples.dll') publish-assets $inputDir (Join-Path $site 'samples')
        if ($LASTEXITCODE -ne 0) { throw 'Publishing sample assets failed.' }
    } finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
    $sampleFiles = @(Get-ChildItem (Join-Path $site 'samples') -File)
    if (!(Test-Path (Join-Path $site 'samples\manifest.json')) -or !(Test-Path (Join-Path $site 'samples\27-full-collection.pdf'))) { throw 'Sample manifest or full collection is missing.' }
    Write-Host ("Sample assets: {0} files, {1:N0} bytes added (lazily fetched, not precached)" -f $sampleFiles.Count, ($sampleFiles | Measure-Object Length -Sum).Sum)
}
$wasm = @(Get-ChildItem (Join-Path $site '_framework') -Filter '*.wasm')
if ($wasm.Count -eq 0) { throw 'No WebAssembly artifacts were published.' }
foreach ($file in $wasm) {
    $stream = [IO.File]::OpenRead($file.FullName)
    try {
        $magic = New-Object byte[] 4
        if ($stream.Read($magic, 0, 4) -ne 4 -or [BitConverter]::ToString($magic) -ne '00-61-73-6D') {
            throw "Invalid WASM artifact: $($file.Name)"
        }
    } finally { $stream.Dispose() }
}
Write-Host "Pages site: $site ($($wasm.Count) valid WASM artifacts; base $BasePath)"
