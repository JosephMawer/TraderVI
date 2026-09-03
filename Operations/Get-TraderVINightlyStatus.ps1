[CmdletBinding()]
param(
    [string] $StateDirectory = (Join-Path $env:LOCALAPPDATA 'TraderVI\Nightly'),
    [switch] $IncludeLogTail
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$statusPath = Join-Path $StateDirectory 'status.json'
if (-not (Test-Path -LiteralPath $statusPath -PathType Leaf)) {
    Write-Host "No nightly run status exists yet at $statusPath"
    exit 3
}

$status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
$status | ConvertTo-Json -Depth 12

if ($IncludeLogTail -and $status.logPath -and (Test-Path -LiteralPath $status.logPath -PathType Leaf)) {
    Write-Host "`n--- Last 80 log lines ---"
    Get-Content -LiteralPath $status.logPath -Tail 80
}

switch ($status.state) {
    'Succeeded' { exit 0 }
    'Attention' { exit 2 }
    'Running' { exit 4 }
    default { exit 1 }
}
