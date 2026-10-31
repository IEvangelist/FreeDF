param(
    [Parameter(Mandatory)] [string] $Site,
    [Parameter(Mandatory)] [string] $Manifest,
    [switch] $Verify
)
# Writes or verifies a SHA-256 manifest of every file in the published site so later jobs
# can prove they validate and deploy exactly the bytes the build job produced.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $Site).Path.TrimEnd('\', '/')
$current = Get-ChildItem $root -Recurse -File -Force | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.FullName.Substring($root.Length + 1).Replace('\', '/')
} | Sort-Object { $_.Substring(66) } -CaseSensitive
if (!$Verify) {
    Set-Content -LiteralPath $Manifest -Value $current -Encoding utf8
    Write-Host "Manifest: $($current.Count) files"
    return
}
$expected = @(Get-Content -LiteralPath $Manifest)
$diff = Compare-Object $expected @($current)
if ($diff) {
    $diff | Select-Object -First 20 | ForEach-Object { Write-Host "$($_.SideIndicator) $($_.InputObject)" }
    throw 'Site does not match the manifest produced by the build job.'
}
Write-Host "Site verified against manifest ($($expected.Count) files)."
