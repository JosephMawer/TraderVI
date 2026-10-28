param([switch]$Run, [switch]$SelfTest)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Fixed, date-bounded read-only study. No SQL, credentials, broker calls or strategy changes.
$sessionOpen = [DateTimeOffset]::Parse('2026-10-28T09:30:00-04:00')
$sessionClose = $sessionOpen.AddHours(6.5)
$stopAt = $sessionClose.AddMinutes(5)
$endpoints = @(1..30) + @(91..105) + @(211..225) + @(361..390)

function Measure-Response($rows, [DateTimeOffset]$started, [DateTimeOffset]$received,
    [DateTimeOffset]$open, [DateTimeOffset]$close, [hashtable]$first) {
    $valid = @(); $invalid = 0; $forming = 0; $revisions = 0; $new = 0
    foreach ($r in $rows) {
        try {
            foreach ($field in @('dateTime','open','high','low','close','volume')) {
                if (!$r.Contains($field) -or $null -eq $r[$field]) { throw 'Missing field' }
            }
            if ([string]$r.dateTime -notmatch '(Z|[+-]\d{2}:\d{2})$') { throw 'Timestamp requires an explicit offset' }
            $start = [DateTimeOffset]::Parse($r.dateTime, [Globalization.CultureInfo]::InvariantCulture)
            $end = $start.AddMinutes(1)
            $o = [decimal]$r.open; $h = [decimal]$r.high; $l = [decimal]$r.low; $c = [decimal]$r.close
            $volume = [decimal]$r.volume
            if ($start.Ticks % [TimeSpan]::TicksPerMinute -ne 0 -or $start -lt $open -or $end -gt $close -or
                $start -gt $received -or $l -le 0 -or $h -lt $l -or $o -lt $l -or $o -gt $h -or
                $c -lt $l -or $c -gt $h -or $volume -lt 0 -or $volume -ne [decimal]::Truncate($volume)) { throw 'Invalid row' }
            # Completion is based on request start, never on elapsed time while fetching.
            if ($end -ge $started) { $forming++; continue }
            $values = @($o,$h,$l,$c,$volume) | ForEach-Object { $_.ToString('G29', [Globalization.CultureInfo]::InvariantCulture) }
            $valid += @{ Start = $start; End = $end; Fingerprint = ($values -join '|') }
        } catch { $invalid++ }
    }
    $groups = @($valid | Group-Object { $_.Start.UtcDateTime.ToString('o') })
    $duplicates = @($groups | Where-Object Count -gt 1).Count
    $complete = @($groups | Where-Object Count -eq 1 | ForEach-Object { $_.Group[0] } | Sort-Object { $_.End })
    foreach ($bar in $complete) {
        $key = $bar.Start.UtcDateTime.ToString('o')
        if (!$first.ContainsKey($key)) {
            $first[$key] = @{ Fingerprint = $bar.Fingerprint; FirstCompletedReceivedUtc = $received.ToString('o');
                FirstRequestUtc = $started.ToString('o'); ArrivalAgeSeconds = ($received - $bar.End).TotalSeconds; Conflict = $false }
            $new++
        } elseif ($first[$key].Fingerprint -ne $bar.Fingerprint) {
            $first[$key].Conflict = $true; $revisions++
        }
    }
    $boundary = [DateTimeOffset]::FromUnixTimeSeconds($started.ToUnixTimeSeconds() - $started.ToUnixTimeSeconds() % 60)
    $available = @($complete | ForEach-Object { $_.End.ToUnixTimeSeconds() })
    $missing = @(); $streak = 0; $maxStreak = 0
    for ($end = $open.AddMinutes(1); $end -le $boundary -and $end -le $close; $end = $end.AddMinutes(1)) {
        if ($available -notcontains $end.ToUnixTimeSeconds()) {
            $missing += $end.ToString('o'); $streak++; $maxStreak = [Math]::Max($maxStreak, $streak)
        } else { $streak = 0 }
    }
    $latest = if ($complete.Count) { $complete[-1].End } else { $null }
    return @{ RawRows = @($rows).Count; InvalidRows = $invalid; FormingRows = $forming;
        DuplicateTimestamps = $duplicates; CompletedRows = $complete.Count; NewCompletedRows = $new;
        ChangedCompletedRows = $revisions; MissingEndsUtc = $missing; MaxMissingStreak = $maxStreak;
        LatestEndUtc = if ($latest) { $latest.ToString('o') } else { $null };
        LatestAgeSeconds = if ($latest) { ($received - $latest).TotalSeconds } else { $null };
        LatestTwoPresent = ($available -contains $boundary.ToUnixTimeSeconds() -and $available -contains $boundary.AddMinutes(-1).ToUnixTimeSeconds()) }
}

function Save-Json([string]$path, $value) {
    $temp = $path + '.writing'
    [IO.File]::WriteAllText($temp, ($value | ConvertTo-Json -Depth 15))
    [IO.File]::Move($temp, $path, $true)
}

if ($SelfTest) {
    $rows = @(0..3 | ForEach-Object { @{ dateTime = $sessionOpen.AddMinutes($_).ToString('o'); open = 10; high = 11; low = 9; close = 10; volume = 100 } })
    $first = @{}
    $a = Measure-Response $rows $sessionOpen.AddMinutes(3).AddSeconds(10) $sessionOpen.AddMinutes(3).AddSeconds(12) $sessionOpen $sessionClose $first
    if ($a.CompletedRows -ne 3 -or $a.FormingRows -ne 1 -or !$a.LatestTwoPresent -or $a.LatestAgeSeconds -ne 12) { throw 'Completion/age test failed' }
    $key = $sessionOpen.UtcDateTime.ToString('o')
    $saved = $first[$key].FirstCompletedReceivedUtc
    $b = Measure-Response $rows $sessionOpen.AddMinutes(3).AddSeconds(20) $sessionOpen.AddMinutes(3).AddSeconds(21) $sessionOpen $sessionClose $first
    if ($b.NewCompletedRows -ne 0 -or $first[$key].FirstCompletedReceivedUtc -ne $saved) { throw 'First-arrival immutability failed' }
    $c = Measure-Response @($rows[0],$rows[2]) $sessionOpen.AddMinutes(3).AddSeconds(10) $sessionOpen.AddMinutes(3).AddSeconds(12) $sessionOpen $sessionClose @{}
    if ($c.MissingEndsUtc.Count -ne 1 -or $c.LatestTwoPresent) { throw 'Gap test failed' }
    $d = Measure-Response @($rows[0],$rows[0]) $sessionOpen.AddMinutes(3).AddSeconds(10) $sessionOpen.AddMinutes(3).AddSeconds(12) $sessionOpen $sessionClose @{}
    if ($d.DuplicateTimestamps -ne 1 -or $d.CompletedRows -ne 0) { throw 'Ambiguous duplicate test failed' }
    $e = Measure-Response @($rows[2]) $sessionOpen.AddMinutes(2).AddSeconds(59) $sessionOpen.AddMinutes(3).AddSeconds(2) $sessionOpen $sessionClose @{}
    if ($e.CompletedRows -ne 0 -or $e.FormingRows -ne 1) { throw 'Request-start completion test failed' }
    $changed = $rows[0].Clone(); $changed.close = 11
    $f = Measure-Response @($changed) $sessionOpen.AddMinutes(3).AddSeconds(20) $sessionOpen.AddMinutes(3).AddSeconds(21) $sessionOpen $sessionClose $first
    if ($f.ChangedCompletedRows -ne 1 -or !$first[$key].Conflict) { throw 'Revision test failed' }
    $restored = ($first | ConvertTo-Json -Depth 8 | ConvertFrom-Json -AsHashtable)
    $null = Measure-Response @($rows[0]) $sessionOpen.AddMinutes(4).AddSeconds(10) $sessionOpen.AddMinutes(4).AddSeconds(11) $sessionOpen $sessionClose $restored
    if (!$restored[$key].Conflict) { throw 'Restart conflict preservation failed' }
    if ($endpoints.Count -ne 90 -or @($endpoints | Select-Object -Unique).Count -ne 90 -or $endpoints[-1] -ne 390) { throw 'Budget/window test failed' }
    'Eight offline qualification checks passed; no network, SQL or broker calls.'
    return
}
if (!$Run) { throw 'Specify -SelfTest for offline validation or -Run for the authorized fixed-date study.' }
$now = [DateTimeOffset]::UtcNow
if ($now -lt $sessionOpen.AddMinutes(-5) -or $now -ge $stopAt) { throw 'Outside October 28 authorized study window' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$folder = Join-Path $root 'artifacts/minute-source-check-20261028'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$owner = [IO.File]::Open((Join-Path $folder 'study.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$http = $null
try {
    $target = Get-Content (Join-Path $folder 'target-private.json') -Raw | ConvertFrom-Json
    if ($target.Symbol -notmatch '^[A-Z0-9.\-]{1,20}$' -or $target.Symbol -in @('XIU','RY')) { throw 'A distinct reviewed TMX candidate symbol is required' }
    $symbols = @('XIU','RY',[string]$target.Symbol)
    $statePath = Join-Path $folder 'study-state-private.json'
    $state = if (Test-Path $statePath) { Get-Content $statePath -Raw | ConvertFrom-Json -AsHashtable } else {
        @{ Version = 1; Symbols = $symbols; Requests = 0; LastStarts = @{}; FinishedSlots = @{}; First = @{} }
    }
    if ($state.Version -ne 1 -or ($state.Symbols -join ',') -ne ($symbols -join ',') -or $state.Requests -lt 0 -or $state.Requests -gt 270) { throw 'Invalid study checkpoint or changed target' }
    $source = Get-Content (Join-Path $root 'Core/TMX/TmxClient.cs') -Raw
    $query = [regex]::Match($source, 'Query = @"\s*(query getTimeSeriesData\([\s\S]*?)",\s*Variables').Groups[1].Value
    if (!$query -or $query -match '\bmutation\b') { throw 'Read-only TMX query extraction failed' }
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false; $handler.UseCookies = $false; $handler.UseDefaultCredentials = $false
    $http = [Net.Http.HttpClient]::new($handler)
    $http.DefaultRequestHeaders.Add('Origin','https://money.tmx.com')
    $http.DefaultRequestHeaders.Add('Referer','https://money.tmx.com/')
    $http.DefaultRequestHeaders.UserAgent.ParseAdd('Mozilla/5.0')
    foreach ($endpoint in $endpoints) {
        $due = $sessionOpen.AddMinutes($endpoint).AddSeconds(10)
        $slot = [string]$endpoint
        if ($state.FinishedSlots.ContainsKey($slot)) { continue }
        while ([DateTimeOffset]::UtcNow -lt $due) {
            Start-Sleep -Milliseconds ([int][Math]::Min(30000, [Math]::Max(1, ($due - [DateTimeOffset]::UtcNow).TotalMilliseconds)))
        }
        if ([DateTimeOffset]::UtcNow -ge $due.AddSeconds(50)) {
            $state.FinishedSlots[$slot] = 'Missed'; Save-Json $statePath $state; continue
        }
        # Mark before external work. A crash leaves an attempted slot, never an automatic retry.
        $state.FinishedSlots[$slot] = 'Attempted'; Save-Json $statePath $state
        foreach ($symbol in $symbols) {
            if ($state.Requests -ge 270) { break }
            if ($state.LastStarts.ContainsKey($symbol)) {
                $earliest = [DateTimeOffset]::Parse($state.LastStarts[$symbol]).AddSeconds(60)
                while ([DateTimeOffset]::UtcNow -lt $earliest -and [DateTimeOffset]::UtcNow -lt $due.AddSeconds(50)) { Start-Sleep -Milliseconds 100 }
            }
            $started = [DateTimeOffset]::UtcNow
            if ($started -ge $due.AddSeconds(50) -or $started -ge $stopAt) { continue }
            $state.Requests++; $state.LastStarts[$symbol] = $started.ToString('o'); Save-Json $statePath $state
            $capture = Join-Path $folder ($started.ToString('yyyyMMddTHHmmssfffZ') + '-' + $symbol)
            $result = @{ Symbol = $symbol; RequestedUtc = $started.ToString('o'); ScheduledEndUtc = $sessionOpen.AddMinutes($endpoint).ToString('o'); Status = 'Failed' }
            $request = $null; $response = $null; $cancel = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20))
            try {
                $body = @{ operationName = 'getTimeSeriesData'; query = $query; variables = @{ symbol = $symbol; interval = 1;
                    startDateTime = $sessionOpen.ToUnixTimeSeconds(); endDateTime = $started.ToUnixTimeSeconds() - $started.ToUnixTimeSeconds() % 60 } } | ConvertTo-Json -Depth 4
                [IO.File]::WriteAllText($capture + '-request-private.json', $body)
                $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, 'https://app-money.tmx.com/graphql')
                $request.Content = [Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
                $response = $http.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cancel.Token).GetAwaiter().GetResult()
                $stream = $response.Content.ReadAsStreamAsync($cancel.Token).GetAwaiter().GetResult()
                $memory = [IO.MemoryStream]::new()
                try {
                    $buffer = [byte[]]::new(16384)
                    while (($read = $stream.ReadAsync($buffer, 0, $buffer.Length, $cancel.Token).GetAwaiter().GetResult()) -gt 0) {
                        if ($memory.Length + $read -gt 1048576) { throw 'Oversize response' }
                        $memory.Write($buffer, 0, $read)
                    }
                    $bytes = $memory.ToArray()
                } finally { $memory.Dispose(); $stream.Dispose() }
                $received = [DateTimeOffset]::UtcNow
                [IO.File]::WriteAllBytes($capture + '-response-private.json', $bytes)
                $result.ReceivedUtc = $received.ToString('o'); $result.HttpStatus = [int]$response.StatusCode
                $result.HttpDate = [string]$response.Headers.Date; $result.HttpAge = [string]$response.Headers.Age
                if (!$response.IsSuccessStatusCode) { throw 'HTTP failure' }
                $parsed = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json -AsHashtable
                if (($parsed.Contains('errors') -and $parsed.errors) -or !$parsed.Contains('data') -or
                    $null -eq $parsed.data -or !$parsed.data.Contains('getTimeSeriesData') -or $null -eq $parsed.data.getTimeSeriesData) { throw 'GraphQL/data failure' }
                if (!$state.First.ContainsKey($symbol)) { $state.First[$symbol] = @{} }
                $result.Measurement = Measure-Response @($parsed.data.getTimeSeriesData) $started $received $sessionOpen $sessionClose $state.First[$symbol]
                $result.Status = 'Received'
            } catch { $result.FailureClass = $_.Exception.GetType().Name } finally {
                if ($response) { $response.Dispose() }; if ($request) { $request.Dispose() }; $cancel.Dispose()
            }
            Save-Json ($capture + '-receipt.json') $result
            Save-Json $statePath $state
        }
        $state.FinishedSlots[$slot] = 'Finished'; Save-Json $statePath $state
    }
    Save-Json (Join-Path $folder 'study-complete.json') @{ FinishedUtc = [DateTimeOffset]::UtcNow.ToString('o'); Requests = $state.Requests;
        FinishedSlots = $state.FinishedSlots; SourceQualified = $false; Note = 'Diagnostic evidence only; coverage, revisions, venue and capacity require review.' }
    'Fixed-date TMX study finished. Private receipts saved; no strategy/source activation.'
} finally { if ($http) { $http.Dispose() }; $owner.Dispose() }
