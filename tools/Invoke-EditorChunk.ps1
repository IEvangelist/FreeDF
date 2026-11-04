param(
    [Parameter(Mandatory)]
    [ValidateSet('node', 'dotnet-editor', 'dotnet-codecs', 'dotnet-render', 'default', 'forms', 'authoring', 'boot', 'viewport', 'a11y', 'responsive', 'profile-invariants', 'perf')]
    [string] $Chunk,
    [string] $Site = (Join-Path $PSScriptRoot 'FreeDF.EditorWasm\bin\pages\wwwroot'),
    [string] $Work = (Join-Path ([IO.Path]::GetTempPath()) 'editor-chunks')
)
# One CI validation chunk. CI (.github/workflows/editor-pages.yml) and local/agent runs share this
# script so a single chunk can be reproduced without running the whole pipeline. See docs/ci.md.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repo
$smokeProject = 'tools/FreeDF.EditorSmoke'
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
    'dotnet-editor' = 'FullyQualifiedName~Editor|FullyQualifiedName~ExistingObjectTransform|FullyQualifiedName~PageTreeTests|FullyQualifiedName~NamedDestinationTests|FullyQualifiedName~LexerKeywordTests'
    'dotnet-codecs' = 'FullyQualifiedName~Jpeg|FullyQualifiedName~Jpx'
    'dotnet-render' = 'FullyQualifiedName~RenderTests|FullyQualifiedName~PngStreamingTests|FullyQualifiedName~ImageDecoderFastPathTests'
}
if ($unitFilters.ContainsKey($Chunk)) {
    Run $Chunk { dotnet test tests/FreeDF.Tests -c Release --filter $unitFilters[$Chunk] }
    return
}
if ($Chunk -eq 'node') {
    Run 'worker cache and scheduling invariants' { node --test (Get-ChildItem tools/FreeDF.EditorWasm/tests -Filter *.test.mjs).FullName }
    $cjs = @(Get-ChildItem tools/FreeDF.EditorWasm, tools/FreeDF.EditorWeb -Filter *.test.cjs | Where-Object Name -notlike 'e2e_*')
    if ($cjs) { Run 'host, worker and selection invariants' { node --test $cjs.FullName } }
    Run 'sample, outline and publish bundle integrity' { node --test tools/FreeDF.EditorWeb/samples.test.mjs tools/FreeDF.EditorWeb/outline_map.test.mjs tools/bundle-editor-site.test.mjs }
    Run 'build-driven deployment and visible test failures' { node --test tools/editor-pages.test.cjs }
    return
}
Prepare
$showcase = Join-Path $samples '01-spec-showcase.pdf'
$repacked = Join-Path $samples '26-repacked-object-streams.pdf'
$profilePdfs = @($showcase, (Join-Path $samples '03-typography-embedded.pdf'), (Join-Path $samples '09-images.pdf'), (Join-Path $samples '20-report.pdf'))
switch ($Chunk) {
    'default' {
        $env:EDITOR_SMOKE_TRANSIENT_BOOT = '1'
        $env:EDITOR_PERF_REPORT_ONLY = '1'
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-save-overlap'
        Run 'local editor and trim-safe JSON round trips' { Smoke @($Site) }
    }
    'forms' { Run 'on-page form filling and field authoring' { Smoke @('--forms', $Site) } }
    'authoring' {
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-interactions'
        $env:EDITOR_PERF_REPORT_ONLY = '1'
        Run 'stable selection, dragging and sidebar focus' { Smoke @('--interactions', $Site) }
        Remove-Item Env:EDITOR_PERF_REPORT_ONLY
        Run 'New document preview and choice controls' { Smoke @('--choices', $Site) }
        Run 'Merge PDFs flow' { Smoke @('--merge', $Site) }
        Run 'one-click sample collection' { Smoke @('--samples', $Site) }
        Run 'annotation authoring' { Smoke @('--annotations', $Site) }
        Run 'object transformations and styling' { Smoke @('--object-breadth', $Site) }
        Run 'local file saving and font access' { Smoke @('--file-access', $Site) }
        Run 'previous-version local data migration' { Smoke @('--storage-migration', $Site) }
        Run 'Contents panel and active page' { Smoke @('--outline', $Site) }
    }
    'boot' {
        Run 'deployment path and styled desktop/mobile shell' { Smoke @('--shell', $Site, (WorkDir 'editor-shell')) }
        Run 'cold boot and injected resource failures' { Smoke @('--boot', $Site, (WorkDir 'editor-boot')) }
        $workerWork = WorkDir 'editor-worker-migration'
        Run 'offline worker migration and rollback' { Smoke @('--service-worker-migration', $Site, $workerWork) }
        Remove-Item -LiteralPath (Join-Path $workerWork 'site') -Recurse -Force
        $updateWork = WorkDir 'editor-update-feedback'
        Run 'immediate update feedback and safe reload' { Smoke @('--update-feedback', $Site, $updateWork) }
        Remove-Item -LiteralPath (Join-Path $updateWork 'site') -Recurse -Force
    }
    'viewport' {
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-viewport'
        Run 'viewport fit and window scrolling' { Smoke @('--viewport', $Site, $repacked) }
        Run 'page-image continuity' { Smoke @('--page-image', $Site, $repacked, (WorkDir 'editor-page-image')) }
    }
    'a11y' {
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-a11y'
        Run 'axe-core tooling (test-only, never published)' { npm ci --prefix tools/FreeDF.EditorSmoke/a11y }
        Run 'accessibility' { Smoke @('--a11y', $Site, $showcase) }
    }
    'responsive' {
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-responsive'
        Run 'responsive layout matrix' { Smoke @('--responsive', $Site, $showcase) }
    }
    'profile-invariants' {
        # Blocking: runs the profile workload with timing report-only, so only functional invariants
        # (e.g. overlay-only edits leave bare-page pixels exact) can fail. Timing gates live in 'perf'.
        $env:EDITOR_PROFILE_REPORT_ONLY = '1'
        Run 'profile functional invariants' { Smoke (@('--profile', $Site, (Join-Path $Work 'editor-profile.json')) + $profilePdfs) }
    }
    'perf' {
        # Non-blocking (scheduled workflow): thresholds are unchanged, a regression is reported not deployed against.
        $report = Join-Path $Work 'editor-profile.json'
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-interactions'
        $failed = @()
        try { Run 'interaction frame and pointer-handler ceilings' { Smoke @('--interactions', $Site) } }
        catch { $failed += $_.Exception.Message; Write-Host "::error::$($_.Exception.Message)" }
        $env:EDITOR_SMOKE_ARTIFACTS = WorkDir 'editor-save-overlap'
        try { Run 'save-overlap input-to-frame ceiling' { Smoke @($Site) } }
        catch { $failed += $_.Exception.Message; Write-Host "::error::$($_.Exception.Message)" }
        $env:EDITOR_PROFILE_REPORT_ONLY = '1'
        try { Run 'strict end-to-end performance goals' { Smoke (@('--profile', $Site, $report) + $profilePdfs) } }
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