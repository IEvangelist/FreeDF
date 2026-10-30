param(
    [string] $Output = (Join-Path $PSScriptRoot 'AdoBye.EditorWasm\bin\pages'),
    [string] $BasePath = '/csharp-pdf/',
    [switch] $Aot,
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
foreach ($asset in @('editor.css', 'editor.js', 'host.js', 'worker.js', 'recovery.js', 'bitstream-vera-license.txt', '_framework\dotnet.js')) {
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
Write-Host "Pages site: $site ($($wasm.Count) valid WASM artifacts; base $BasePath)"
