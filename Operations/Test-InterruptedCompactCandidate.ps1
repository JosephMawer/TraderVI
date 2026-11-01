# Run only against a prepared candidate, from the workspace containing its isolated build.
[CmdletBinding()]param([Parameter(Mandatory)][guid]$Token)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$folder = (Resolve-Path (Join-Path $root "artifacts/restart-release/$($Token.ToString('N'))")).Path
$binary = (Resolve-Path (Join-Path $root 'artifacts/compact-candidate/bin/TraderVI.Engine/release/TraderVI.Engine.dll')).Path
if ($Token -eq [guid]::Empty -or !(Test-Path -LiteralPath (Join-Path $folder 'broker-copy.json'))) { throw 'Missing prepared rehearsal.' }
$capability = & dotnet $binary --candidate-capabilities
if ($LASTEXITCODE -ne 0 -or $capability -cne 'compact-localdb-only-v1') { throw 'Expected the isolated no-send build.' }

function Invoke-Candidate([string]$Mode) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = 'Hidden'
    $start.WorkingDirectory = $root
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($binary)
    $start.ArgumentList.Add('--run')
    $start.Environment['TRADERVI_CANDIDATE_REHEARSAL'] = $Token.ToString('N')
    $start.Environment.Remove('TRADERVI_REHEARSAL_INTERRUPT') | Out-Null
    if ($Mode -eq 'interrupt') { $start.Environment['TRADERVI_REHEARSAL_INTERRUPT'] = 'before-sql-commit' }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($start)
    try {
        $line = $p.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(30)).GetAwaiter().GetResult()
        $ms = $clock.Elapsed.TotalMilliseconds
        if ($Mode -eq 'interrupt') {
            if ($line -cne 'PROTECTIVE_RESERVATION_BEFORE_SQL_COMMIT') {
                if ($p.HasExited) { throw ("Candidate missed the interruption boundary: " + $p.StandardError.ReadToEnd() + $line) }
                throw 'Candidate missed the interruption boundary.'
            }
            $p.Kill()
            if (!$p.WaitForExit(10000)) {throw 'Candidate did not stop.'}
            return
        }
        if (!$p.WaitForExit(10000)) { throw 'Candidate did not finish.' }
        if ($Mode -eq 'hold') {
            if ($p.ExitCode -ne 1 -or $line) { throw 'Unexpected admission with an exceptional checkpoint.' }
            return
        }
        if ($p.ExitCode -ne 0) { throw ("Candidate stopped: " + $p.StandardError.ReadToEnd()) }
        $r = $line | ConvertFrom-Json
        $first = $Mode -eq 'resume'
        if (!$r.ready -or $r.interrupted -ne $first -or $r.eligible -ne [int]$first -or !$r.sameBuys -or
            $r.reserved -ne 100 -or $r.booked -ne 0 -or $r.sellShares -ne 10 -or $r.buyReads -ne 1 -or
            $r.accountReads -ne 1 -or $r.submissions -ne 0 -or $r.symbolRows -lt 2) { throw 'Recovery decision mismatch.' }
        $r | Add-Member -NotePropertyName launchToDecisionMs -NotePropertyValue $ms -PassThru
    } finally {
        if (!$p.HasExited) { $p.Kill(); $p.WaitForExit() }
        $p.Dispose()
    }
}

Invoke-Candidate 'interrupt'
$resume = Invoke-Candidate 'resume'
$repeat = Invoke-Candidate 'repeat'
# An actual missing local witness must require recovery even after an otherwise successful restart.
$witness = Join-Path $folder 'witness.dat'
$aside = Join-Path $folder 'witness.negative-control'
[IO.File]::Move($witness, $aside)
try { Invoke-Candidate 'hold' } finally { [IO.File]::Move($aside, $witness) }
Invoke-Candidate 'hold' # Restoring the file cannot clear the saved incident.
$result = [ordered]@{ resumed=$resume; repeated=$repeat; missingWitnessBlocked=$true; existingHoldPreserved=$true }
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $folder 'interrupted-result.json')
$result | ConvertTo-Json -Depth 4
