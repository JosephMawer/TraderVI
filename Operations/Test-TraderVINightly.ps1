[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo "artifacts/nightly-tests/$([Guid]::NewGuid().ToString('N'))"
[void] [IO.Directory]::CreateDirectory($fixture)
$script:LogPath = Join-Path $fixture 'test.log'
$script:StatusPath = Join-Path $fixture 'status.json'
$script:Assertions = 0
function Assert($condition, [string] $message) {
    if (-not $condition) { throw "FAILED: $message" }
    $script:Assertions++
}
function Assert-Throws([scriptblock] $Action, [string] $Pattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_.Exception.Message }
    Assert ($null -ne $caught -and $caught -match $Pattern) "Expected failure: $Pattern; received: $caught"
}
# Load function definitions only: the operational runner's main block must never execute in tests.
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Invoke-TraderVINightly.ps1'), [ref]$null, [ref]$parseErrors)
Assert ($parseErrors.Count -eq 0) 'Runner parses in Windows PowerShell.'
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
. (Join-Path $PSScriptRoot 'NightlyBuild.ps1')
. (Join-Path $PSScriptRoot 'NightlyDataAudit.ps1')
& git init --quiet $fixture
if ($LASTEXITCODE) { throw 'Fixture Git initialization failed.' }
[IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), "artifacts/`n*.log`nstatus.json`n")
[IO.File]::WriteAllText((Join-Path $fixture 'tracked.cs'), 'original')
[IO.File]::WriteAllText((Join-Path $fixture 'deleted.cs'), 'removed later')
& git -C $fixture add .
& git -C $fixture -c user.name=NightlyTests -c user.email=nightly@example.invalid commit --quiet --no-gpg-sign -m fixture
if ($LASTEXITCODE) { throw 'Fixture commit failed.' }
[IO.File]::WriteAllText((Join-Path $fixture 'tracked.cs'), 'uncommitted')
[IO.File]::WriteAllText((Join-Path $fixture 'new.cs'), 'untracked')
# Explicit single fixture file, no recursive removal.
Remove-Item -LiteralPath (Join-Path $fixture 'deleted.cs')
$run = Join-Path $fixture 'artifacts/capture'
$copy = New-NightlySourceCopy $fixture $run 3 0
Assert ((Get-Content (Join-Path $copy.root 'tracked.cs') -Raw) -eq 'uncommitted') 'Includes uncommitted edits.'
Assert (Test-Path (Join-Path $copy.root 'new.cs')) 'Includes untracked source.'
Assert (-not (Test-Path (Join-Path $copy.root 'deleted.cs'))) 'Preserves tracked deletions.'
Assert (-not (Test-Path (Join-Path $copy.root 'artifacts'))) 'Excludes ignored output.'
Assert ($copy.manifest.source.workingTreeState -eq 'Dirty') 'Records real dirty state.'
[IO.File]::WriteAllText((Join-Path $fixture 'tracked.cs'), 'later development')
Assert-NightlyCopy $copy.manifest $copy.root
Assert ((Get-Content (Join-Path $copy.root 'tracked.cs') -Raw) -eq 'uncommitted') 'Later development cannot modify copy.'
[IO.File]::WriteAllText((Join-Path $copy.root 'tracked.cs'), 'tampered')
Assert-Throws { Assert-NightlyCopy $copy.manifest $copy.root } 'Source copy changed'
Assert-Throws { Resolve-ConfiguredPath $fixture '../outside.cs' } 'escapes'

# Force an edit between copying and the second source manifest.
$script:OriginalAssertCopy = ${function:Assert-NightlyCopy}
$script:ChangesRemaining = 1
function Assert-NightlyCopy($Manifest, $CopyRoot) {
    & $script:OriginalAssertCopy $Manifest $CopyRoot
    if ($script:ChangesRemaining -gt 0) {
        [IO.File]::AppendAllText((Join-Path $fixture 'tracked.cs'), ' changed')
        $script:ChangesRemaining--
    }
}
$retryCopy = New-NightlySourceCopy $fixture (Join-Path $fixture 'artifacts/retry') 3 0
Assert ($retryCopy.attempts -eq 2) 'Retries a changing source capture.'
$script:ChangesRemaining = 3
Assert-Throws { New-NightlySourceCopy $fixture (Join-Path $fixture 'artifacts/exhausted') 3 0 } 'Source changed during capture'
Assert ($script:ChangesRemaining -eq 0) 'Capture retries are bounded.'
Set-Item Function:Assert-NightlyCopy $script:OriginalAssertCopy

function Result([int] $ExitCode=0, [string] $Output='') {
    [pscustomobject]@{ Completed=$true; ExitCode=$ExitCode; StandardOutput=$Output; StandardError='' }
}
function AuditJson([object[]] $Findings=@(), [int] $WarningSessions=2) {
    return @{ SchemaVersion=1; Run=@{
        StartedUtc='2026-10-28T05:00:00Z'; CompletedUtc='2026-10-28T05:01:00Z'
        Options=@{StaleWarningSessions=$WarningSessions; StaleErrorSessions=5; SectorMappingMaxAgeDays=14}
        Report=@{MarketDataAsOf='2026-10-27T00:00:00'; TotalSymbols=10; ActiveSymbols=8; ActiveStocks=7; ActiveEtfs=1
            Findings=@($Findings); ErrorCount=@($Findings | Where-Object Severity -eq 'Error').Count
            WarningCount=@($Findings | Where-Object Severity -eq 'Warning').Count }
    }} | ConvertTo-Json -Depth 10 -Compress
}
Assert (Test-TransientRestoreFailure (Result 1 'NU1301: unavailable')) 'Network restore error is retryable.'
Assert (-not (Test-TransientRestoreFailure (Result 1 'NU1004 lock changed; NU1301'))) 'Lock mismatch cannot retry as network error.'
Assert (-not (Test-TransientRestoreFailure (Result 1 'NU1403 hash mismatch'))) 'Package hash mismatch is not transient.'
Assert (-not (Test-TransientRestoreFailure (Result 1 'error CS0103'))) 'Compiler errors are not transient.'

$config = Get-Content (Join-Path $PSScriptRoot 'nightly-runner.json') -Raw | ConvertFrom-Json
$config.build.retryDelaySeconds = 0
foreach ($stage in $config.stages) {
    $project = Join-Path $fixture $stage.project
    [void] [IO.Directory]::CreateDirectory((Split-Path -Parent $project))
    [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"/>')
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $project) 'packages.lock.json'), '{}')
}
[void] [IO.Directory]::CreateDirectory((Join-Path $fixture 'Operations'))
[IO.File]::WriteAllText((Join-Path $fixture 'Operations/nightly-nuget.config'), '<configuration/>')
function New-TestStatus {
    [pscustomobject]@{ runId=[Guid]::NewGuid().ToString('D'); stages=@(); build=[pscustomobject]@{
        state='Pending'; startedUtc=$null; completedUtc=$null; durationSeconds=$null; message=''
        runDirectory=$null; sourceDirectory=$null; sourceBefore=$null; sourceAfter=$null
        captureAttempts=0; sdkVersion=$null; packageDirectory=$null; restores=@(); projects=@(); artifacts=@()
    } }
}
$script:Mode = 'Transient'
$script:StageOverride = $null
$script:StageOverrideName = 'Hermes'
$script:Calls = @()
function Invoke-CapturedProcess($FileName, $Arguments, $WorkingDirectory, $TimeoutMinutes, $Label, $EnvironmentOverrides=@{}) {
    $script:Calls += [pscustomobject]@{ arguments=$Arguments; workingDirectory=$WorkingDirectory; environment=$EnvironmentOverrides }
    if ($Arguments[0] -eq '--version') { return Result 0 '10.0.401' }
    if ($Arguments[0] -eq 'restore') {
        if ($script:Mode -eq 'Transient') { $script:Mode='Success'; return Result 1 'NU1301' }
        if ($script:Mode -eq 'Locked') { return Result 1 'NU1004' }
        if ($script:Mode -eq 'Network') { return Result 1 'NU1301' }
        return Result
    }
    if ($Arguments[0] -eq 'build') {
        if ($script:Mode -eq 'Compiler') { return Result 1 'CS0103' }
        $stage = $config.stages | Where-Object { $Arguments[1] -eq (Join-Path $WorkingDirectory $_.project) }
        $entry = Join-Path $WorkingDirectory $stage.entryAssembly
        [void] [IO.Directory]::CreateDirectory((Split-Path -Parent $entry))
        [IO.File]::WriteAllText($entry, 'synthetic assembly')
        [IO.File]::AppendAllText((Join-Path $fixture 'tracked.cs'), ' editor')
        return Result
    }
    if ($null -ne $script:StageOverride -and $Arguments[0] -match ($script:StageOverrideName + '[.]dll$')) { return $script:StageOverride }
    if ($Arguments[0] -match 'DataAudit[.]dll$') { return Result 0 (AuditJson) }
    return Result
}
$status = New-TestStatus
$built = Invoke-LatestSourceBuild $config $fixture 'synthetic-dotnet' $status
Assert ($status.build.state -eq 'Succeeded' -and $built.Count -eq 4) 'All four isolated builds succeed despite ongoing edits.'
Assert ($status.build.environmentFiles.Count -eq 4) 'MSBuild parent imports and SDK discovery are bounded.'
Assert ($status.build.restores.Count -eq 5) 'Exactly one transient restore retry.'
Assert (@($script:Calls | Where-Object { $_.arguments[0] -eq 'restore' -and $_.arguments -notcontains '--locked-mode' }).Count -eq 0) 'Every restore is locked.'
Assert (@($script:Calls | Where-Object { $_.workingDirectory -eq $fixture }).Count -eq 0) 'Build commands never use live source directory.'
Assert ($status.stages.Count -eq 0) 'Preparation launches no operational stage.'
$stageResult = Invoke-NightlyStage $config.stages[0] $status.build.sourceDirectory 'synthetic-dotnet' $built.Hermes $status
Assert ($stageResult.state -eq 'Succeeded') 'Verified artifact starts once.'
Assert ($script:Calls[-1].environment.TRADERVI_CODE_VERSION -eq $status.build.sourceBefore.gitCommit) 'Stage receives captured commit provenance.'
Assert ($script:Calls[-1].environment.TRADERVI_WORKING_TREE_STATE -eq 'Dirty') 'Stage receives captured working-tree state.'
# Exercise the same stage loop used by the scheduled runner, with no operational processes.
foreach ($scenario in @(
    @{ result=(Result 0 'HermesSymbolUnavailable: sector-metadata WM: No results'); expected='Attention'; stages=4 },
    @{ result=(Result 0 'HermesSymbolUnavailable: daily-bars ABC: source returned no usable data.'); expected='Attention'; stages=4 },
    @{ result=(Result 2 'HermesSymbolUnavailable: sector-metadata WM: No results'); expected='Failed'; stages=1 },
    @{ result=(Result 1 'HermesSymbolUnavailable: daily-bars ABC: timeout'); expected='Failed'; stages=1 },
    @{ result=(Result 0 'HermesSymbolUnavailable: daily-bars ABC: timeout; Sector index collection failed: unavailable'); expected='Failed'; stages=1 },
    @{ result=(Result 0 'Failed: 1'); expected='Failed'; stages=1 },
    @{ result=([pscustomobject]@{Completed=$false; ExitCode=124; StandardOutput='HermesSymbolUnavailable: daily-bars ABC: timeout'; StandardError=''}); expected='Failed'; stages=1 }
)) {
    $script:StageOverride = $scenario.result
    $pipeline = New-TestStatus
    $pipeline.build = $status.build
    $outcome = Invoke-NightlyStages $config $pipeline 'synthetic-dotnet' $built
    Assert ($pipeline.stages[0].state -eq $scenario.expected) "Hermes classifies $($scenario.expected) correctly."
    Assert ($pipeline.stages.Count -eq $scenario.stages) 'Ticker warnings continue; fatal conditions stop later stages.'
    Assert ($outcome.HasFailure -eq ($scenario.expected -eq 'Failed')) 'Pipeline retains fatal outcome.'
    if ($scenario.expected -eq 'Attention') {
        Assert $outcome.HasAttention 'Warnings remain visible in final outcome.'
        Assert (($pipeline.stages.name -join ',') -eq 'Hermes,DataAudit,Delphi,Athena') 'Warning preserves all four stages in order.'
    }
}
foreach ($scenario in @(
    @{ name='Delphi'; result=(Result 0 'audit=Invalid'); expected='Failed' },
    @{ name='Delphi'; result=(Result 0 'audit=Degraded'); expected='Attention' },
    @{ name='Delphi'; result=(Result 1 'audit=Degraded'); expected='Failed' },
    @{ name='Athena'; result=(Result 2); expected='Attention' }
)) {
    $script:StageOverrideName = $scenario.name
    $script:StageOverride = $scenario.result
    $pipeline = New-TestStatus
    $pipeline.build = $status.build
    $outcome = Invoke-NightlyStages $config $pipeline 'synthetic-dotnet' $built
    $observed = $pipeline.stages | Where-Object name -eq $scenario.name
    Assert ($observed.state -eq $scenario.expected) "$($scenario.name) retains its existing audit/exit contract."
    Assert ($pipeline.stages.Count -eq 4) 'Athena still runs after a failed Delphi publication.'
    Assert ($outcome.HasFailure -eq ($scenario.expected -eq 'Failed')) 'A rejected publication cannot become pipeline success.'
}
$warning = @{Severity='Warning'; Code='BARS.ACTIVE_SYMBOL_LAGGING'; Symbol='ABC'; Message='Two sessions behind.'}
$errorFinding = @{Severity='Error'; Code='BARS.ACTIVE_SYMBOL_STALE'; Symbol='ABC'; Message='Five sessions behind.'}
$otherFinding = @{Severity='Warning'; Code='SECTOR.ACTIVE_STOCK_MAPPING_MISSING'; Symbol='XYZ'; Message='Mapping missing.'}
$script:StageOverrideName = 'DataAudit'
foreach ($scenario in @(
    @{result=(Result 0 (AuditJson)); state='Succeeded'},
    @{result=(Result 1 (AuditJson @($warning))); state='Attention'},
    @{result=(Result 2 (AuditJson @($errorFinding))); state='Attention'},
    @{result=(Result 3 'Database unavailable'); state='Failed'},
    @{result=(Result 0 'not JSON'); state='Failed'},
    @{result=(Result 0 (AuditJson @($errorFinding))); state='Failed'},
    @{result=([pscustomobject]@{Completed=$false; ExitCode=124; StandardOutput=(AuditJson); StandardError=''}); state='Failed'}
)) {
    $script:StageOverride = $scenario.result
    $pipeline = New-TestStatus; $pipeline.build = $status.build
    $baseline = Join-Path $fixture 'data-audit-last-completed.json'
    $priorHash = (Get-FileHash $baseline).Hash
    $outcome = Invoke-NightlyStages $config $pipeline 'synthetic-dotnet' $built
    $auditStage = $pipeline.stages | Where-Object name -eq 'DataAudit'
    Assert ($auditStage.state -eq $scenario.state) 'Audit findings and execution failure are distinct.'
    Assert (($pipeline.stages.name -join ',') -eq 'Hermes,DataAudit,Delphi,Athena') 'Audit cannot suppress independent downstream checks.'
    Assert ($outcome.HasFailure -eq ($scenario.state -eq 'Failed')) 'Audit failure remains a failed night.'
    if ($scenario.state -eq 'Failed') { Assert ((Get-FileHash $baseline).Hash -eq $priorHash) 'Incomplete or malformed audit cannot advance baseline.' }
    else { Assert ($auditStage.dataAudit.Run.Report.Findings.Count -eq ($scenario.result.StandardOutput | ConvertFrom-Json).Run.Report.Findings.Count) 'Full findings are retained in nightly status.' }
}
Assert (@($script:Calls | Where-Object { $_.arguments[0] -match 'DataAudit[.]dll$' -and $_.arguments -contains '--json' }).Count -gt 0) 'Nightly requests structured CLI report.'
# Use a fresh private state directory for deterministic report comparison tests.
$comparisonDirectory = Join-Path $fixture 'artifacts/comparison'
[void][IO.Directory]::CreateDirectory($comparisonDirectory)
$savedStatusPath = $script:StatusPath; $script:StatusPath = Join-Path $comparisonDirectory 'status.json'
$first = Save-NightlyDataAuditReport (AuditJson @($warning)) 1
Assert ($first.Comparison.State -eq 'Baseline' -and $first.Comparison.NewFindings.Count -eq 0) 'First scan establishes a baseline without inventing changes.'
$changed = Save-NightlyDataAuditReport (AuditJson @($errorFinding,$otherFinding)) 2
Assert ($changed.Comparison.NewFindings.Count -eq 1 -and $changed.Comparison.NewFindings[0].Symbol -eq 'XYZ') 'New issue is identified by code and symbol.'
Assert ($changed.Comparison.WorsenedFindings.Count -eq 1 -and $changed.Comparison.ResolvedFindings.Count -eq 0) 'Lagging to stale is escalation, not resolution plus a new issue.'
$same = Save-NightlyDataAuditReport (AuditJson @($errorFinding,$otherFinding)) 2
Assert ($same.Comparison.NewFindings.Count -eq 0 -and $same.Comparison.WorsenedFindings.Count -eq 0) 'Unchanged findings do not create change alerts.'
$clear = Save-NightlyDataAuditReport (AuditJson) 0
Assert ($clear.Comparison.ResolvedFindings.Count -eq 2) 'Findings no longer observed remain explicit.'
$differentOptions = Save-NightlyDataAuditReport (AuditJson @() 3) 0
Assert ($differentOptions.Comparison.State -eq 'Unavailable') 'Changed thresholds cannot imply improvement.'
$corruptBaseline = Join-Path $comparisonDirectory 'data-audit-last-completed.json'
[IO.File]::WriteAllText($corruptBaseline, 'broken')
$unavailable = Save-NightlyDataAuditReport (AuditJson) 0
Assert ($unavailable.Comparison.State -eq 'Unavailable') 'Unreadable baseline is disclosed.'
$wrongCounts = (AuditJson) | ConvertFrom-Json; $wrongCounts.Run.Report.ErrorCount = 1
Assert-Throws { Save-NightlyDataAuditReport ($wrongCounts | ConvertTo-Json -Depth 10) 2 } 'counts do not match'
$script:StatusPath = $savedStatusPath
$script:StageOverride = $null
$script:StageOverrideName = 'Hermes'
[IO.File]::AppendAllText((Join-Path $status.build.sourceDirectory $config.stages[0].entryAssembly), 'tampered')
$beforeCalls = $script:Calls.Count
Assert-Throws { Invoke-NightlyStage $config.stages[0] $status.build.sourceDirectory 'synthetic-dotnet' $built.Hermes $status } 'changed after preflight'
Assert ($script:Calls.Count -eq $beforeCalls) 'Artifact tampering cannot launch a process.'
foreach ($mode in @('Locked', 'Network', 'Compiler')) {
    $script:Mode=$mode; $script:Calls=@(); $failed=New-TestStatus
    Assert-Throws { Invoke-LatestSourceBuild $config $fixture 'synthetic-dotnet' $failed } 'failed'
    Assert ($failed.stages.Count -eq 0) "$mode failure starts no operational stage."
    if ($mode -eq 'Network') { Assert ($failed.build.restores.Count -eq 3) 'Restore retries stop after three attempts.' }
    if ($mode -eq 'Locked') { Assert ($failed.build.restores.Count -eq 1) 'Lock mismatch stops immediately.' }
}
Remove-Item -LiteralPath (Join-Path $fixture 'Hermes/packages.lock.json')
Assert-Throws { Assert-NightlyDependencyLocks (Join-Path $fixture 'Hermes/Hermes.csproj') $fixture @{} } 'lock missing'
Write-Host "Passed $script:Assertions nightly regression assertions. No operational programs or external services ran."
