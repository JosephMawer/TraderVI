[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Engine', 'Collector', 'Dashboard')][string]$Application,
    [string]$ConfigurationPath = (Join-Path $env:LOCALAPPDATA 'TraderVI\Launchers\applications.json'),
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configuration = Get-Content -LiteralPath $ConfigurationPath -Raw | ConvertFrom-Json
if ($configuration.SchemaVersion -ne 1 -or $configuration.Repository -ne $repo) { throw 'Application configuration does not match this checkout.' }
if (!(Test-Path -LiteralPath $configuration.CalendarPath -PathType Leaf)) { throw 'Reviewed calendar missing.' }
$runtime = Join-Path $repo 'TraderVI.WPF\bin\Release\net10.0-windows'
$processName = switch ($Application) {
    Engine { 'TraderVI.Engine' }
    Collector { 'TraderVI.Collector' }
    Dashboard { 'TraderVI.WPF' }
}
$executable = if ($Application -eq 'Collector') {
    Join-Path $repo 'TraderVI.Collector\bin\Release\net10.0\TraderVI.Collector.exe'
} else { Join-Path $runtime ($processName + '.exe') }
if (!(Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Build output missing: $executable" }
if ($Application -eq 'Collector') {
    $manifest = Get-Content -LiteralPath $configuration.CollectorManifest -Raw | ConvertFrom-Json
    if ($manifest.ExecutablePath -ne $executable) { throw 'Collector must use the normal Release output.' }
    & (Join-Path $PSScriptRoot 'Collector\Watch-Collector.ps1') -ManifestPath $configuration.CollectorManifest -ValidateOnly
}
if ($ValidateOnly) { return }
if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) { throw "$Application is already running. No second instance started." }
$env:TRADERVI_TSX_CALENDAR_PATH = $configuration.CalendarPath
$stateDirectory = Split-Path -Parent $ConfigurationPath
$receiptPath = Join-Path $stateDirectory ($Application.ToLowerInvariant() + '-run.json')
$receipt = [ordered]@{ Application=$Application; StartedUtc=[DateTime]::UtcNow.ToString('o'); LauncherId=$PID; ProcessId=$null; ExitCode=$null; State='Starting' }
function Save-Receipt { $receipt | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8 }
Save-Receipt
try {
    if ($Application -eq 'Collector') {
        # Keep the task active for the supervisor's full lifetime, including bounded restarts.
        $receipt.State = 'Supervising'; Save-Receipt
        & (Join-Path $PSScriptRoot 'Collector\Watch-Collector.ps1') -ManifestPath $configuration.CollectorManifest
        $receipt.ExitCode = 0
    } else {
        $arguments = @{ FilePath=$executable; WorkingDirectory=$runtime; PassThru=$true; WindowStyle='Hidden' }
        if ($Application -eq 'Engine') { $arguments.ArgumentList = '--run' }
        else { $arguments.WindowStyle = 'Normal' }
        $child = Start-Process @arguments
        $receipt.ProcessId = $child.Id; $receipt.State = 'Running'; Save-Receipt
        $child.WaitForExit()
        $receipt.ExitCode = $child.ExitCode
        if ($child.ExitCode -ne 0) { throw "$Application exited unsuccessfully. Inspect application diagnostics." }
    }
    $receipt.State = 'Exited'; Save-Receipt
} catch {
    $receipt.State = 'Failed'; Save-Receipt
    throw
}
