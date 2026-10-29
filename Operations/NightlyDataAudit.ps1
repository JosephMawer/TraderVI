# Reporting only: reads completed process output and private prior reports, never SQL or providers.
function ConvertFrom-DataAuditReport {
    param([string] $Json)
    $document = $Json | ConvertFrom-Json
    if ($document.SchemaVersion -notin @(1,2)) { throw 'Unsupported DataAudit report schema.' }
    $run = $document.Run
    if ([datetime]$run.CompletedUtc -lt [datetime]$run.StartedUtc) { throw 'Invalid audit timestamps.' }
    $options = $run.Options
    if ($options.StaleWarningSessions -lt 1 -or $options.StaleErrorSessions -lt $options.StaleWarningSessions -or $options.SectorMappingMaxAgeDays -lt 1) { throw 'Invalid audit options.' }
    $report = $run.Report
    if ($null -eq $report.Findings -or $report.TotalSymbols -lt 0 -or $report.ActiveSymbols -lt 0 -or $report.ActiveSymbols -gt $report.TotalSymbols) { throw 'Invalid audit report.' }
    foreach ($finding in $report.Findings) {
        if ($finding.Severity -notin @('Warning','Error') -or [string]::IsNullOrWhiteSpace($finding.Code) -or [string]::IsNullOrWhiteSpace($finding.Message)) { throw 'Invalid audit finding.' }
    }
    if ($report.ErrorCount -ne @($report.Findings | Where-Object Severity -eq 'Error').Count -or
        $report.WarningCount -ne @($report.Findings | Where-Object Severity -eq 'Warning').Count) { throw 'Audit counts do not match findings.' }
    return $document
}

function Get-DataAuditFindingKey {
    param($Finding)
    # Staleness uses different codes at warning/error thresholds but remains the same issue.
    $code = if ($Finding.Code -in @('BARS.ACTIVE_SYMBOL_LAGGING','BARS.ACTIVE_SYMBOL_STALE')) { 'BARS.ACTIVE_SYMBOL_FRESHNESS' } else { $Finding.Code }
    return ConvertTo-Json -InputObject @($code, $Finding.Symbol) -Compress
}

function Save-NightlyDataAuditReport {
    param([string] $Json, [int] $ExitCode)
    $current = ConvertFrom-DataAuditReport $Json
    $report = $current.Run.Report
    $expectedExit = if ($report.ErrorCount -gt 0) { 2 } elseif ($report.WarningCount -gt 0) { 1 } else { 0 }
    if ($ExitCode -ne $expectedExit) { throw 'Audit exit code disagrees with report.' }
    $baselinePath = Join-Path (Split-Path -Parent $script:StatusPath) 'data-audit-last-completed.json'
    $previous = $null
    $comparison = [pscustomobject]@{
        State='Baseline'; PreviousCompletedUtc=$null; NewFindings=@(); WorsenedFindings=@(); ResolvedFindings=@()
        Message='First completed audit; comparison baseline established.'
    }
    if (Test-Path -LiteralPath $baselinePath) {
        try { $previous = ConvertFrom-DataAuditReport (Get-Content -LiteralPath $baselinePath -Raw) }
        catch {
            $comparison.State = 'Unavailable'
            $comparison.Message = 'Previous audit report is unreadable; no change comparison is claimed.'
            Write-RunLog $comparison.Message
        }
    }
    if ($null -ne $previous) {
        $comparison.PreviousCompletedUtc = $previous.Run.CompletedUtc
        $sameOptions = $true
        foreach ($name in @('StaleWarningSessions','StaleErrorSessions','SectorMappingMaxAgeDays')) {
            if ($current.Run.Options.$name -ne $previous.Run.Options.$name) { $sameOptions = $false }
        }
        if ($current.SchemaVersion -ne $previous.SchemaVersion) {
            $comparison.State = 'Unavailable'
            $comparison.Message = 'Audit rule/report versions differ; baseline refreshed without claiming resolved data problems.'
        }
        elseif (-not $sameOptions -or $null -eq $report.MarketDataAsOf -or $null -eq $previous.Run.Report.MarketDataAsOf -or
            [datetime]$report.MarketDataAsOf -lt [datetime]$previous.Run.Report.MarketDataAsOf) {
            $comparison.State = 'Unavailable'
            $comparison.Message = 'Audit options or market dates are not comparable; no change comparison is claimed.'
        }
        else {
            $old = @{}
            foreach ($finding in $previous.Run.Report.Findings) {
                $key = Get-DataAuditFindingKey $finding
                if (-not $old.ContainsKey($key) -or $finding.Severity -eq 'Error') { $old[$key] = $finding }
            }
            $seen = @{}
            foreach ($finding in $report.Findings) {
                $key = Get-DataAuditFindingKey $finding
                $seen[$key] = $true
                if (-not $old.ContainsKey($key)) { $comparison.NewFindings += $finding }
                elseif ($old[$key].Severity -eq 'Warning' -and $finding.Severity -eq 'Error') { $comparison.WorsenedFindings += $finding }
            }
            foreach ($key in $old.Keys) { if (-not $seen.ContainsKey($key)) { $comparison.ResolvedFindings += $old[$key] } }
            $comparison.State = 'Compared'
            $comparison.Message = "$($comparison.NewFindings.Count) new; $($comparison.WorsenedFindings.Count) escalated from warning to error; $($comparison.ResolvedFindings.Count) no longer reported since $($comparison.PreviousCompletedUtc)."
        }
    }
    # Only a complete, validated scan advances the baseline. Full findings remain in status and run log.
    Write-JsonAtomically -Value $current -Path $baselinePath
    $current | Add-Member -NotePropertyName Comparison -NotePropertyValue $comparison
    return $current
}
