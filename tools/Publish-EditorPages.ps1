param(
    [string] $Output = (Join-Path $PSScriptRoot 'AdoBye.EditorWasm\bin\pages'),
    [string] $BasePath = '/csharp-pdf/',
    [switch] $Aot,
    [switch] $Trim,
    [switch] $KillServiceWorker,
    [string] $ArtifactsPath,
    [string[]] $MsbuildArgs = @()
)
$ErrorActionPreference = 'Stop'
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
foreach ($asset in @('editor.css', 'editor.js', 'host.js', 'worker.js', 'recovery.js', 'sw.js', 'sw-client.js', 'bitstream-vera-license.txt', '_framework\dotnet.js')) {
    if (!(Test-Path (Join-Path $site $asset))) { throw "Missing site asset: $asset" }
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
# Versioned precache manifest: exact file set and digests of this build, embedded in the service worker.
$swPath = Join-Path $site 'sw.js'
$siteRoot = (Resolve-Path $site).Path.TrimEnd('\') + '\'
$sha = [Security.Cryptography.SHA256]::Create()
$precache = @(Get-ChildItem $site -Recurse -File | Where-Object { $_.Name -notin @('sw.js', '.nojekyll') -and $_.Extension -notin @('.br', '.gz') } |
    Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ url = $_.FullName.Substring($siteRoot.Length).Replace('\', '/'); hash = [Convert]::ToBase64String($sha.ComputeHash([IO.File]::ReadAllBytes($_.FullName))) }
    })
$template = [IO.File]::ReadAllText($swPath)
if (!$template.Contains('const MANIFEST = null; /* __PRECACHE_MANIFEST__ */')) { throw 'sw.js is missing the precache manifest placeholder.' }
$identity = $template + (($precache | ForEach-Object { "$($_.url) $($_.hash)" }) -join "`n")
$version = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity))).Replace('-', '').ToLowerInvariant()).Substring(0, 16)
$manifest = if ($KillServiceWorker) { '{"kill":true}' } else { [ordered]@{ version = $version; files = $precache } | ConvertTo-Json -Depth 4 -Compress }
[IO.File]::WriteAllText($swPath, $template.Replace('const MANIFEST = null; /* __PRECACHE_MANIFEST__ */', "const MANIFEST = $manifest;"))
Write-Host "Service worker precache: $($precache.Count) files, version $version"
Write-Host "Pages site: $site ($($wasm.Count) valid WASM artifacts; base $BasePath)"
