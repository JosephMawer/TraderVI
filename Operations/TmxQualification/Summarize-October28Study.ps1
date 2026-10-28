$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$folder = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/minute-source-check-20261028'))
$state = Get-Content (Join-Path $folder 'study-state-private.json') -Raw | ConvertFrom-Json -AsHashtable
$open = [DateTimeOffset]::Parse('2026-10-28T09:30:00-04:00')
$now = [DateTimeOffset]::UtcNow
$expected = @(@(1..30) + @(91..105) + @(211..225) + @(361..390) | Where-Object { $open.AddMinutes($_) -lt $now })
$results = foreach ($symbol in $state.Symbols) {
    $first = if ($state.First.ContainsKey($symbol)) { $state.First[$symbol] } else { @{} }
    $ages = @(); $missing = 0; $conflicts = 0
    foreach ($minute in $expected) {
        $key = $open.AddMinutes($minute - 1).UtcDateTime.ToString('o')
        if ($first.ContainsKey($key)) {
            $ages += [double]$first[$key].ArrivalAgeSeconds
            if ($first[$key].Conflict) { $conflicts++ }
        } else { $missing++ }
    }
    [pscustomobject]@{ Symbol = $symbol; ExpectedElapsedStudyEndpoints = $expected.Count;
        FirstArrivalsWithin15Seconds = @($ages | Where-Object { $_ -le 15 }).Count;
        FirstArrivalsWithin30Seconds = @($ages | Where-Object { $_ -le 30 }).Count;
        FirstArrivalsWithin45Seconds = @($ages | Where-Object { $_ -le 45 }).Count;
        FirstArrivalsBeforeNextMinute = @($ages | Where-Object { $_ -lt 60 }).Count;
        LaterArrivals = @($ages | Where-Object { $_ -ge 60 }).Count; Missing = $missing; Conflicted = $conflicts;
        OtherHistoricalRows = $first.Count - $ages.Count }
}
[pscustomobject]@{ CheckedUtc = $now; AttemptedRequests = $state.Requests;
    MissedSamplingSlots = @($state.FinishedSlots.Values | Where-Object { $_ -eq 'Missed' }).Count;
    InterruptedSamplingSlots = @($state.FinishedSlots.Values | Where-Object { $_ -eq 'Attempted' }).Count;
    Symbols = @($results); SourceQualified = $false } | ConvertTo-Json -Depth 5
