<#
.SYNOPSIS  Runs the profiling harnesses over a corpus directory and appends JSON lines to a results file.
.EXAMPLE   .\tools\profile\Invoke-CorpusProfile.ps1 -Corpus $env:CORPUS -Out results.jsonl -Session my-session
Each engine/file pair is ONE process (clean peak-memory + GC counters) and ONE hold of the shared validation slot.
#>
param(
    [Parameter(Mandatory)] [string] $Corpus,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Session = $env:COMPUTERNAME,
    [string[]] $Engines = @('adobye', 'pdfpig', 'pdfium', 'mupdf', 'pikepdf'),
    [string] $Ops = 'open,text,render,thumb,save',
    [int] $Iters = 5, [int] $Pages = 8, [int] $Budget = 60,
    [string] $Slot = 'E:\GitHub\copilot-worktrees\.validation-slot',
    [string] $Filter = '*.pdf', [int] $Affinity = 0
)
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$exe = Join-Path $root 'tools\AdoBye.Profile\bin\Release\net10.0\AdoBye.Profile.exe'
$py = Join-Path $PSScriptRoot 'profile_engines.py'

function Use-Slot([scriptblock] $Body) {
    while ($true) {
        try {
            $fs = [IO.File]::Open($Slot, 'CreateNew', 'Write', 'None')
            $b = [Text.Encoding]::UTF8.GetBytes($Session); $fs.Write($b, 0, $b.Length); $fs.Dispose(); break
        } catch [IO.IOException] { Start-Sleep -Seconds 15 }
    }
    try { & $Body } finally { if ((Get-Content $Slot -Raw -ea 0) -eq $Session) { Remove-Item $Slot -Force } }
}

Get-ChildItem $Corpus -Recurse -Filter $Filter -File | Sort-Object Length | ForEach-Object {
    $f = $_.FullName
    foreach ($e in $Engines) {
        Use-Slot {
            $p = if ($e -in 'adobye', 'pdfpig') {
                $a = @($f, '--engine', $e, '--ops', $Ops, '--iters', $Iters, '--pages', $Pages, '--budget', $Budget)
                if ($e -eq 'pdfpig' -and $Ops -notlike '*text_all*') { $a[4] = 'open,text' }
                Start-Process $exe -ArgumentList $a -NoNewWindow -PassThru -RedirectStandardOutput "$Out.tmp"
            } else {
                $o = if ($e -eq 'pikepdf') { 'open,save' } else { $Ops }
                Start-Process python -ArgumentList @($py, $e, $f, '--ops', $o, '--iters', $Iters, '--pages', $Pages, '--budget', $Budget) -NoNewWindow -PassThru -RedirectStandardOutput "$Out.tmp"
            }
            if ($Affinity) { $p.ProcessorAffinity = [IntPtr]$Affinity }
            if (-not $p.WaitForExit(($Budget * 8 + 120) * 1000)) { Stop-Process -Id $p.Id -Force; Add-Content $Out (@{ engine = $e; file = $_.Name; op = 'timeout' } | ConvertTo-Json -Compress) }
            Get-Content "$Out.tmp" | Add-Content $Out
        }
        Write-Host "done $e $($_.Name)"
        Start-Sleep -Seconds 5
    }
}
Remove-Item "$Out.tmp" -ea 0
