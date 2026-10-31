param(
    [Parameter(Mandatory)]
    [ValidateSet('node', 'dotnet-editor', 'dotnet-codecs', 'dotnet-render', 'default', 'forms', 'authoring', 'boot', 'viewport', 'a11y', 'responsive', 'profile')]
    [string] $Chunk,
    [string] $Site = (Join-Path $PSScriptRoot 'AdoBye.EditorWasm\bin\pages\wwwroot'),
    [string] $Work = (Join-Path ([IO.Path]::GetTempPath()) 'editor-chunks')
)
# One CI validation chunk. CI (.github/workflows/editor-pages.yml) and local/agent runs share this
# script so a single chunk can be reproduced without running the whole pipeline. See docs/ci.md.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repo
$smokeProject = 'tools/AdoBye.EditorSmoke'
$samples = Join-Path $repo 'samples'
function Run([string] $What, [scriptblock] $Block) {
    Write-Host "::group::$What"
    try { $global:LASTEXITCODE = 0; & $Block; if ($LASTEXITCODE) { throw "$What failed (exit $LASTEXITCODE)." } } finally { Write-Host '::endgroup::' }
}
function Smoke([string[]] $Mode) { dotnet run --no-build -c Release --project $smokeProject -- @Mode }
function WorkDir([string] $Name) { $p = Join-Path $Work $Name; New-Item -ItemType Directory -Force $p | Out-Null; $p }
New-Item -ItemType Directory -Force $Work | Out-Null
function Prepare {
    if (!(Test-Path (Join-Path $Site 'index.html'))) { throw "Published site not found at $Site. Run tools/Publish-EditorPages.ps1 first or pass -Site." }
    dotnet build $smokeProject -c Release --nologo -v q
    if ($LASTEXITCODE) { throw 'Building the smoke runner failed.' }
}
$unitFilters = @{
    'dotnet-editor' = 'FullyQualifiedName~Editor|FullyQualifiedName~ExistingObjectTransform'
    'dotnet-codecs' = 'FullyQualifiedName~Jpeg|FullyQualifiedName~Jpx'
    'dotnet-render' = 'FullyQualifiedName~RenderTests|FullyQualifiedName~PngStreamingTests|FullyQualifiedName~ImageDecoderFastPathTests'
}
if ($unitFilters.ContainsKey($Chunk)) {
    Run $Chunk { dotnet test tests/AdoBye.Tests -c Release --filter $unitFilters[$Chunk] }
    return
}
if ($Chunk -eq 'node') {
    Run 'worker cache and scheduling invariants' { node --test (Get-ChildItem tools/AdoBye.EditorWasm/tests -Filter *.test.mjs).FullName }
    $cjs = @(Get-ChildItem tools/AdoBye.EditorWasm, tools/AdoBye.EditorWeb -Filter *.test.cjs | Where-Object Name -notlike 'e2e_*')
    if ($cjs) { Run 'host, worker and selection invariants' { node --test $cjs.FullName } }
    Run 'sample loader integrity' { node --test tools/AdoBye.EditorWeb/samples.test.mjs }
    return
}
Prepare
$showcase = Join-Path $samples '01-spec-showcase.pdf'
$repacked = Join-Path $samples '26-repacked-object-streams.pdf'
switch ($Chunk) {
    'default' {
        $env:EDITOR_SMOKE_TRANSIENT_BOOT = '1'
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-save-overlap'
        Run 'local editor and trim-safe JSON round trips' { Smoke @($Site) }
    }
    'forms' { Run 'on-page form filling and field authoring' { Smoke @('--forms', $Site) } }
    'authoring' {
        Run 'Merge PDFs flow' { Smoke @('--merge', $Site) }
        Run 'one-click sample collection' { Smoke @('--samples', $Site) }
        Run 'annotation authoring' { Smoke @('--annotations', $Site) }
    }
    'boot' { Run 'cold boot and injected resource failures' { Smoke @('--boot', $Site, (WorkDir 'editor-boot')) } }
    'viewport' {
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-viewport'
        Run 'viewport fit and window scrolling' { Smoke @('--viewport', $Site, $repacked) }
        Run 'page-image continuity' { Smoke @('--page-image', $Site, $repacked, (WorkDir 'editor-page-image')) }
    }
    'a11y' {
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-a11y'
        Run 'axe-core tooling (test-only, never published)' { npm ci --prefix tools/AdoBye.EditorSmoke/a11y }
        Run 'accessibility' { Smoke @('--a11y', $Site, $showcase) }
    }
    'responsive' {
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-responsive'
        Run 'responsive layout matrix' { Smoke @('--responsive', $Site, $showcase) }
    }
    'profile' {
        $report = Join-Path $Work 'editor-profile.json'
        $env:EDITOR_PROFILE_REPORT_ONLY = '1'
        $failed = @()
        try { Run 'strict end-to-end performance goals' { Smoke @('--profile', $Site, $report, $showcase, (Join-Path $samples '03-typography-embedded.pdf'), (Join-Path $samples '09-images.pdf'), (Join-Path $samples '20-report.pdf')) } }
        catch { $failed += $_.Exception.Message; Write-Host "::error::$($_.Exception.Message)" }
        Remove-Item Env:EDITOR_PROFILE_REPORT_ONLY
        if (Test-Path $report) {
            try { Run 'CI regressions and open performance goals' { Smoke @('--profile-ci', $report, (Join-Path $repo 'docs/performance/editor-ci-known-misses.v1.json')) } }
            catch { $failed += $_.Exception.Message; Write-Host "::error::$($_.Exception.Message)" }
            try { Run 'gates reject synthetic regressions' { & (Join-Path $PSScriptRoot 'Test-EditorProfileCi.ps1') -Profile $report -Output (Join-Path $Work 'editor-profile-ci-tests') } }
            catch { $failed += $_.Exception.Message; Write-Host "::error::$($_.Exception.Message)" }
        }
        if ($failed) { throw ($failed -join '; ') }
    }
}
