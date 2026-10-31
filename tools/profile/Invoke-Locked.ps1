<#
.SYNOPSIS  Runs ONE command while holding the shared validation slot (atomic create, delete only if it still names this session).
.EXAMPLE   .\tools\profile\Invoke-Locked.ps1 -Session my-session -Command { dotnet test }
#>
param(
    [Parameter(Mandatory)] [string] $Session,
    [Parameter(Mandatory)] [scriptblock] $Command,
    [string] $Slot = 'E:\GitHub\copilot-worktrees\.validation-slot',
    [int] $WaitSeconds = 1800
)
$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ($true) {
    try {
        $fs = [IO.File]::Open($Slot, 'CreateNew', 'Write', 'None')
        $b = [Text.Encoding]::UTF8.GetBytes($Session); $fs.Write($b, 0, $b.Length); $fs.Dispose(); break
    } catch [IO.IOException] {
        if ((Get-Date) -gt $deadline) { throw "slot busy: $(Get-Content $Slot -Raw -ea 0)" }
        Start-Sleep -Seconds 10
    }
}
try { & $Command } finally { if ((Get-Content $Slot -Raw -ea 0) -eq $Session) { Remove-Item $Slot -Force } }
