param(
    [string] $Output = (Join-Path $PSScriptRoot 'AdoBye.EditorWasm\bin\pages'),
    [string] $BasePath = '/csharp-pdf/',
    [switch] $Aot,
    [switch] $SkipSamples,
    [switch] $NoBundle,
    [switch] $Trim,
    [switch] $KillServiceWorker,
    [string] $ArtifactsPath,
    [string[]] $MsbuildArgs = @()
)
$ErrorActionPreference = 'Stop'
$elapsed = [Diagnostics.Stopwatch]::StartNew()
if ($BasePath -notmatch '^/([A-Za-z0-9._-]+/)*$') { throw 'BasePath must start and end with / and contain only path segments.' }
$project = Join-Path $PSScriptRoot 'AdoBye.EditorWasm\AdoBye.EditorWasm.csproj'
$publishArgs = @('publish', $project, '-c', 'Release', '-o', $Output, '--nologo')
if ($Aot) { $publishArgs += @('-p:RunAOTCompilation=true', '-p:PublishTrimmed=true', '-p:WasmEnableSIMD=true') }
if ($Trim -and !$Aot) { $publishArgs += '-p:PublishTrimmed=true' }
$publishArgs += $MsbuildArgs
if ($ArtifactsPath) { $publishArgs += @('--artifacts-path', $ArtifactsPath) }
if (Test-Path (Join-Path $Output 'wwwroot')) { Remove-Item (Join-Path $Output 'wwwroot') -Recurse -Force }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'WebAssembly publish failed.' }
$site = Join-Path $Output 'wwwroot'
$index = Join-Path $site 'index.html'
$engine = '<script>{const w=new Worker(new URL("worker.js",document.baseURI),{type:"module",name:"PDF engine"}),events=[];for(const h of["onmessage","onerror","onmessageerror"])w[h]=e=>events.push([h,e]);window.pdfEngine={worker:w,events}}</script>'
$html = [IO.File]::ReadAllText($index).Replace('<head>', "<head>`n<base href=`"$BasePath`">`n<meta name=`"editor-host`" content=`"wasm`">`n$engine")
[IO.File]::WriteAllText($index, $html)
[IO.File]::WriteAllText((Join-Path $site '.nojekyll'), '')
foreach ($asset in @('editor.css', 'theme.css', 'surface.css', 'ui.js', 'favicon.svg', 'favicon-32.png', 'favicon.ico', 'apple-touch-icon.png', 'site.webmanifest', 'editor.js', 'annotations.js', 'form-fields.js', 'host.js', 'worker.js', 'pool.js', 'scheduler.js', 'replica-worker.js', 'recovery.js', 'samples.js', 'sw.js', 'sw-client.js', 'bitstream-vera-license.txt', '_framework\dotnet.js')) {
    if (!(Test-Path (Join-Path $site $asset))) { throw "Missing site asset: $asset" }
}
# Lazily fetched sample PDFs (never part of startup): 01-26 and 28 are committed; 27 is generated in a scratch folder.
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
# Only the ICU data selected by applicationCulture=en-US is ever fetched; do not ship or precache the CJK/other bundles.
Get-ChildItem (Join-Path $site '_framework') -Filter 'icudt_*' | Where-Object { $_.Name -notlike 'icudt_EFIGS*' } | Remove-Item
# Fewer, larger startup files: merge stylesheets/scripts/modules and inline favicons (see bundle-editor-site.mjs).
if (!$NoBundle) {
    if (!(Test-Path (Join-Path $PSScriptRoot 'node_modules\esbuild'))) {
        npm ci --prefix $PSScriptRoot --no-audit --no-fund --silent
        if ($LASTEXITCODE -ne 0) { throw 'Installing the bundling tools (npm ci in tools/) failed.' }
    }
    node (Join-Path $PSScriptRoot 'bundle-editor-site.mjs') $site
    if ($LASTEXITCODE -ne 0) { throw 'Bundling the editor shell failed.' }
}
# Versioned precache manifest: exact file set and digests of this build, embedded in the service worker.
$swPath = Join-Path $site 'sw.js'
$siteRoot = (Resolve-Path $site).Path.TrimEnd('\') + '\'
$sha = [Security.Cryptography.SHA256]::Create()
$precache = @(Get-ChildItem $site -Recurse -File | Where-Object { $_.Name -notin @('sw.js', '.nojekyll') -and $_.Extension -notin @('.br', '.gz') } |
    Sort-Object FullName | ForEach-Object {
        $url = $_.FullName.Substring($siteRoot.Length).Replace('\', '/')
        [pscustomobject]@{ url = $url; hash = [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($_.FullName))); lazy = $url.StartsWith('samples/') -or $url.StartsWith('fallback-fonts/') }
    })
$template = [IO.File]::ReadAllText($swPath)
if (!$template.Contains('const MANIFEST = null; /* __PRECACHE_MANIFEST__ */')) { throw 'sw.js is missing the precache manifest placeholder.' }
$identity = $template + (($precache | ForEach-Object { "$($_.url) $($_.hash)" }) -join "`n")
$version = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity))).Replace('-', '').ToLowerInvariant()).Substring(0, 16)
$manifest = if ($KillServiceWorker) { '{"kill":true}' } else { [ordered]@{ version = $version; files = $precache } | ConvertTo-Json -Depth 4 -Compress }
[IO.File]::WriteAllText($swPath, $template.Replace('const MANIFEST = null; /* __PRECACHE_MANIFEST__ */', "const MANIFEST = $manifest;"))
Write-Host "Service worker precache: $($precache.Count) files, version $version"
$assets = @(Get-ChildItem $site -Recurse -File)
$bytes = ($assets | Measure-Object -Property Length -Sum).Sum
if ($bytes -ge 1000000000) { throw "Pages site exceeds the 1 GB limit: $bytes bytes." }
Write-Host ("Pages site: {0} ({1} valid WASM artifacts; base {2}; {3} files, {4:N0} bytes; publish {5:N1}s)" -f $site, $wasm.Count, $BasePath, $assets.Count, $bytes, $elapsed.Elapsed.TotalSeconds)
