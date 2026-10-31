[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $TargetCommit,
    [Parameter(Mandatory)][string] $ValidationLock,
    [string] $Url = 'https://ievangelist.github.io/csharp-pdf/',
    [string] $Repository = 'IEvangelist/csharp-pdf',
    [long] $RunId,
    [string] $Output = (Join-Path ([IO.Path]::GetTempPath()) "editor-public-$([guid]::NewGuid().ToString('N'))"),
    [string[]] $FeatureSmoke = @()
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$Output = [IO.Path]::GetFullPath($Output)
$ValidationLock = [IO.Path]::GetFullPath($ValidationLock)
$owner = "editor-public-$PID-$([guid]::NewGuid().ToString('N'))"
$held = $false
$createdOutput = $false
$result = [ordered]@{ target = $TargetCommit; url = $Url; passed = $false; checks = @() }
$oldArtifacts = $env:EDITOR_SMOKE_ARTIFACTS
$oldTransient = $env:EDITOR_SMOKE_TRANSIENT_BOOT
$oldSiteRoot = $env:EDITOR_SMOKE_SITE_ROOT

function Invoke-Checked([string] $Executable, [string[]] $Arguments, [string] $Log) {
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath $Log | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed ($LASTEXITCODE): $($Arguments -join ' '); see $Log" }
}
function Read-GitHub([string[]] $Arguments) {
    $json = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub query failed: gh $($Arguments -join ' ')" }
    return ($json | ConvertFrom-Json)
}

Push-Location $repoRoot
try {
    if (Test-Path $Output) { throw "Use a fresh output directory: $Output" }
    New-Item -ItemType Directory -Path $Output | Out-Null
    $createdOutput = $true
    $entryPoint = Get-Content (Join-Path $PSScriptRoot 'AdoBye.EditorSmoke\Program.cs') -Raw
    foreach ($feature in $FeatureSmoke) {
        if ($feature -notmatch '^--[a-z][a-z0-9-]*$' -or !$entryPoint.Contains("args[0] == `"$feature`"")) {
            throw "Unsupported feature smoke selector in this checkout: $feature"
        }
    }
    $sha = & git rev-parse --verify "$TargetCommit^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Unknown target commit: $TargetCommit" }
    $result.target = $sha
    & git diff --exit-code $sha -- src tools/AdoBye.EditorWeb tools/AdoBye.EditorWasm samples
    if ($LASTEXITCODE -ne 0) { throw 'Check out the target runtime/source before running public acceptance.' }
    $siteUri = [uri] $Url
    if (!$siteUri.IsAbsoluteUri -or $siteUri.Scheme -notin @('http', 'https') -or $siteUri.AbsolutePath -ne '/csharp-pdf/') {
        throw 'Url must be an absolute HTTP(S) URL ending in /csharp-pdf/.'
    }
    if (!$RunId) {
        $runs = Read-GitHub @('run', 'list', '--repo', $Repository, '--workflow', 'editor-pages.yml',
            '--commit', $sha, '--limit', '20', '--json', 'databaseId,conclusion')
        foreach ($candidate in @($runs | Where-Object conclusion -eq 'success')) {
            $run = Read-GitHub @('run', 'view', "$($candidate.databaseId)", '--repo', $Repository, '--json', 'headSha,conclusion,jobs,url')
            if (@($run.jobs | Where-Object { $_.name -eq 'deploy' -and $_.conclusion -eq 'success' }).Count -eq 1) {
                $RunId = $candidate.databaseId
                break
            }
        }
        if (!$RunId) { throw "No successful Pages deployment found for $sha." }
    }
    $run = Read-GitHub @('run', 'view', "$RunId", '--repo', $Repository, '--json', 'headSha,conclusion,jobs,url')
    if ($run.headSha -ne $sha -or $run.conclusion -ne 'success' -or
        @($run.jobs | Where-Object { $_.name -eq 'deploy' -and $_.conclusion -eq 'success' }).Count -ne 1) {
        throw 'The specified run must have successfully deployed the exact target commit.'
    }
    $result.run = $run.url
    $artifact = Join-Path $Output 'deployment'
    Invoke-Checked 'gh' @('run', 'download', "$RunId", '--repo', $Repository, '--name', 'editor-validation-aot', '--dir', $artifact) (Join-Path $Output 'artifact.log')
    $site = Join-Path $artifact 'editor-pages\wwwroot'
    $native = @(Get-ChildItem (Join-Path $site '_framework') -Filter 'dotnet.native*.wasm')
    if ($native.Count -ne 1) { throw 'Expected exactly one AOT native WASM asset in the deployed artifact.' }
    $assets = @(Get-ChildItem $site -Recurse -File)
    $bytes = ($assets | Measure-Object -Property Length -Sum).Sum
    if ($bytes -ge 1000000000) { throw "Published site exceeds the 1 GB Pages limit: $bytes bytes." }
    $result.payload = @{ files = $assets.Count; bytes = $bytes }
    $expected = (Get-FileHash $native[0].FullName -Algorithm SHA256).Hash
    $identityAssets = @("_framework/$($native[0].Name)", 'index.html', 'host.js', 'worker.js', 'editor.js', 'editor.css', 'theme.css', 'surface.css', 'ui.js', 'recovery.js')
    if (Test-Path (Join-Path $site 'samples.js')) { $identityAssets += 'samples.js' }
    foreach ($asset in @('annotations.js', 'form-fields.js', 'pool.js', 'scheduler.js', 'replica-worker.js')) {
        if (Test-Path (Join-Path $site $asset)) { $identityAssets += $asset }
    }
    function Assert-LiveAssets([string] $Name) {
        $destination = Join-Path $Output $Name
        New-Item -ItemType Directory -Path $destination | Out-Null
        foreach ($asset in $identityAssets) {
            $file = Join-Path $destination ([IO.Path]::GetFileName($asset))
            Invoke-WebRequest ([uri]::new($siteUri, $asset)) -OutFile $file
            $actual = (Get-FileHash $file -Algorithm SHA256).Hash
            $artifactHash = (Get-FileHash (Join-Path $site $asset) -Algorithm SHA256).Hash
            if ($actual -ne $artifactHash) { throw "Public asset $asset does not match $sha ($actual != $artifactHash)." }
        }
    }
    Assert-LiveAssets 'identity-before'
    $result.native = @{ asset = $native[0].Name; sha256 = $expected }
    while (!$held) {
        try {
            $stream = [IO.File]::Open($ValidationLock, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $writer = [IO.StreamWriter]::new($stream)
            try { $writer.Write($owner) } finally { $writer.Dispose() }
            $held = $true
        } catch [IO.IOException] {
            if (!(Test-Path $ValidationLock)) { throw }
            Write-Host "Waiting for validation slot: $ValidationLock"
            Start-Sleep 15
        }
    }
    Invoke-Checked 'dotnet' @('build', (Join-Path $PSScriptRoot 'AdoBye.EditorSmoke'), '-c', 'Release', '-t:Rebuild', '--nologo', '-v', 'quiet') (Join-Path $Output 'build.log')
    $runner = Join-Path $PSScriptRoot 'AdoBye.EditorSmoke\bin\Release\net10.0\AdoBye.EditorSmoke.dll'
    $env:EDITOR_SMOKE_TRANSIENT_BOOT = $null
    $env:EDITOR_SMOKE_SITE_ROOT = $site
    $checks = @(
        @{ name = 'full'; arguments = @($Url) },
        @{ name = 'merge'; arguments = @('--merge', $Url) },
        @{ name = 'page-image'; arguments = @('--page-image', $Url, (Join-Path $repoRoot 'samples\26-repacked-object-streams.pdf'), (Join-Path $Output 'page-image')) },
        @{ name = 'boot'; arguments = @('--boot', $Url, (Join-Path $Output 'boot')) }
    )
    foreach ($feature in $FeatureSmoke) {
        $arguments = @($feature, $Url)
        if ($feature -eq '--viewport') { $arguments += Join-Path $repoRoot 'samples\26-repacked-object-streams.pdf' }
        $checks += @{ name = $feature.Substring(2); arguments = $arguments }
    }
    foreach ($check in $checks) {
        $env:EDITOR_SMOKE_ARTIFACTS = Join-Path $Output $check.name
        Invoke-Checked 'dotnet' (@($runner) + $check.arguments) (Join-Path $Output "$($check.name).log")
        $result.checks += $check.name
    }
    Assert-LiveAssets 'identity-after'
    $result.passed = $true
    Write-Host "PASS public acceptance: $sha ($($run.url)); evidence: $Output"
} catch {
    $result.error = $_.Exception.Message
    throw
} finally {
    if ($held -and (Test-Path $ValidationLock) -and [IO.File]::ReadAllText($ValidationLock) -eq $owner) {
        Remove-Item -LiteralPath $ValidationLock
    }
    $env:EDITOR_SMOKE_ARTIFACTS = $oldArtifacts
    $env:EDITOR_SMOKE_TRANSIENT_BOOT = $oldTransient
    $env:EDITOR_SMOKE_SITE_ROOT = $oldSiteRoot
    if ($createdOutput) { $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Output 'acceptance.json') -Encoding utf8 }
    Pop-Location
}
