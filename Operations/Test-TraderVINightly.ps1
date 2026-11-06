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
. (Join-Path $PSScriptRoot 'NightlyOutputs.ps1')
. (Join-Path $PSScriptRoot 'NightlyDataAudit.ps1')
$installerAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Install-TraderVINightlyTask.ps1'), [ref]$null, [ref]$parseErrors)
Assert ($parseErrors.Count -eq 0) 'Installer parses in Windows PowerShell.'
foreach ($definition in $installerAst.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
$nextStart = Get-NightlyNextLocalStart '06:30' ([DateTime]'2026-11-01T12:00:00')
Assert ($nextStart -eq [DateTime]'2026-11-02T06:30:00') 'Installation after the selected hour cannot trigger a missed run today.'
Assert ($nextStart.Kind -eq [DateTimeKind]::Unspecified) 'Task boundary is local wall-clock time, not a fixed timezone offset.'
Assert ((Get-NightlyNextLocalStart '06:30' ([DateTime]'2027-03-14T01:00:00')) -eq [DateTime]'2027-03-14T06:30:00') 'Spring daylight-saving transition preserves local 06:30.'
Assert ((Get-NightlyNextLocalStart '06:30' ([DateTime]'2026-11-01T01:00:00')) -eq [DateTime]'2026-11-01T06:30:00') 'Fall daylight-saving transition preserves local 06:30.'
Assert-Throws { Get-NightlyNextLocalStart '25:30' } 'HH:mm'
$evening = [DateTimeOffset]::Parse('2026-10-28T21:00:00-04:00')
Assert ((Resolve-NightlyPreparationDate '' $evening) -eq '2026-10-28') 'Default uses actual Toronto date.'
Assert ((Resolve-NightlyPreparationDate '2026-10-29' $evening) -eq '2026-10-29') 'Closed-session early preparation is explicit.'
Assert ((Resolve-NightlyPreparationDate '2026-10-29' ([DateTimeOffset]::Parse('2026-10-29T00:30:00-04:00'))) -eq '2026-10-29') 'Preparation remains stable across midnight.'
Assert-Throws { Resolve-NightlyPreparationDate '2026-10-29' ([DateTimeOffset]::Parse('2026-10-28T15:59:59-04:00')) } '16:00'
Assert-Throws { Resolve-NightlyPreparationDate '2026-10-30' $evening } 'today or tomorrow'
Assert-Throws { Resolve-NightlyPreparationDate '2026-10-27' $evening } 'today or tomorrow'
Assert-Throws { Resolve-NightlyPreparationDate '10/29/2026' $evening } 'yyyy-MM-dd'
$completed = [pscustomobject]@{localRunDate='2026-10-28'; preparationDate='2026-10-29'; state='Attention'}
Assert (Test-NightlyPreparationCompleted $completed '2026-10-29') 'Early completion suppresses the following midnight run.'
Assert (-not (Test-NightlyPreparationCompleted $completed '2026-10-30')) 'The subsequent day remains eligible.'
$completed.state='Failed'
Assert (-not (Test-NightlyPreparationCompleted $completed '2026-10-29')) 'A failed run never counts as completed.'
Assert (Test-NightlyPreparationCompleted ([pscustomobject]@{localRunDate='2026-10-28';state='Succeeded'}) '2026-10-28') 'Legacy status retains date suppression.'
[IO.File]::WriteAllText((Join-Path $fixture 'tracked.cs'), 'editable source')
Assert-Throws { Resolve-ConfiguredPath $fixture '../outside.cs' } 'escapes'

function Result([int] $ExitCode=0, [string] $Output='') {
    [pscustomobject]@{ Completed=$true; ExitCode=$ExitCode; StandardOutput=$Output; StandardError='' }
}
function AuditJson([object[]] $Findings=@(), [int] $WarningSessions=2, [int] $SchemaVersion=2) {
    return @{ SchemaVersion=$SchemaVersion; Run=@{
        StartedUtc='2026-10-28T05:00:00Z'; CompletedUtc='2026-10-28T05:01:00Z'
        Options=@{StaleWarningSessions=$WarningSessions; StaleErrorSessions=5; SectorMappingMaxAgeDays=14}
        Report=@{MarketDataAsOf='2026-10-27T00:00:00'; TotalSymbols=10; ActiveSymbols=8; ActiveStocks=7; ActiveEtfs=1
            Findings=@($Findings); ErrorCount=@($Findings | Where-Object Severity -eq 'Error').Count
            WarningCount=@($Findings | Where-Object Severity -eq 'Warning').Count }
    }} | ConvertTo-Json -Depth 10 -Compress
}
$config = Get-Content (Join-Path $PSScriptRoot 'nightly-runner.json') -Raw | ConvertFrom-Json
$manifest = [ordered]@{schemaVersion=1;directoryHashFormat='RelativePathTabSha256OrdinalV1';outputSetId='fixture';sourceCommit=('1' * 40);workingTreeState='Dirty';stages=@()}
$config.outputsManifest = 'selection.json'
foreach ($stage in $config.stages) {
    $stage.entryAssembly = "outputs/$($stage.name)/$($stage.name).dll"
    $entry = Join-Path $fixture $stage.entryAssembly
    $directory = Split-Path -Parent $entry
    [void] [IO.Directory]::CreateDirectory($directory)
    [IO.File]::WriteAllText($entry, 'synthetic assembly')
    [IO.File]::WriteAllText((Join-Path $directory "$($stage.name).deps.json"), '{}')
    [IO.File]::WriteAllText((Join-Path $directory "$($stage.name).runtimeconfig.json"), '{}')
    $snapshot = Get-ArtifactSnapshot $fixture $entry
    $manifest.stages += [pscustomobject]@{name=$stage.name;entryAssembly=$stage.entryAssembly;sha256=$snapshot.sha256;fileCount=$snapshot.fileCount}
}
$manifestPath = Join-Path $fixture $config.outputsManifest
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5))
Test-Configuration $config $fixture
function New-TestStatus {
    [pscustomobject]@{runId=[Guid]::NewGuid().ToString('D');repositoryRoot=$fixture;preparationDate='2026-10-29';stages=@();outputs=[pscustomobject]@{
        sourceCommit=('1' * 40);workingTreeState='Dirty'
    }}
}
$script:StageOverride = $null
$script:StageOverrideName = 'Hermes'
$script:Calls = @()
function Invoke-CapturedProcess($FileName, $Arguments, $WorkingDirectory, $TimeoutMinutes, $Label, $EnvironmentOverrides=@{}) {
    $script:Calls += [pscustomobject]@{arguments=$Arguments;workingDirectory=$WorkingDirectory;environment=$EnvironmentOverrides}
    if ($Arguments[0] -in @('build','restore','publish','run','--version')) { throw 'Unexpected preparation process.' }
    if ($null -ne $script:StageOverride -and $Arguments[0] -match ($script:StageOverrideName + '[.]dll$')) { return $script:StageOverride }
    if ($Arguments[0] -match 'DataAudit[.]dll$') { return Result 0 (AuditJson) }
    return Result
}
$beforeFiles = @(Get-ChildItem -LiteralPath $fixture -Recurse -File).Count
$selected = Get-NightlyExistingOutputs $config $fixture
$built = $selected.artifacts
Assert ($built.Count -eq 4) 'All four existing outputs verified before any process.'
Assert ($script:Calls.Count -eq 0) 'Preflight starts no compiler, restore, SDK probe or application.'
Assert (@(Get-ChildItem -LiteralPath $fixture -Recurse -File).Count -eq $beforeFiles) 'Preflight creates no copies or run folders.'
[IO.File]::AppendAllText((Join-Path $fixture 'tracked.cs'), ' later edit')
$again = Get-NightlyExistingOutputs $config $fixture
Assert ($again.sourceCommit -eq ('1' * 40) -and $again.workingTreeState -eq 'Dirty') 'Later source edits do not replace the compiled-code provenance.'
Assert ($again.artifacts.Hermes.sha256 -eq $built.Hermes.sha256) 'Later source edits leave selected binaries unchanged.'
$config.executionMode='Build'
Assert-Throws { Test-Configuration $config $fixture } 'ExistingOutputs'
$config.executionMode='ExistingOutputs'
$config.schemaVersion=3
Assert-Throws { Test-Configuration $config $fixture } 'schema 4'
$config.schemaVersion=4
$status = New-TestStatus
$stageResult = Invoke-NightlyStage $config.stages[0] $fixture 'synthetic-dotnet' $built.Hermes $status
Assert ($stageResult.state -eq 'Succeeded') 'Verified existing artifact starts once.'
Assert ($script:Calls[-1].arguments[0] -eq (Join-Path $fixture $config.stages[0].entryAssembly)) 'Stage launches the selected DLL in place.'
Assert ($script:Calls[-1].workingDirectory -eq $fixture) 'Working directory remains the repository for established resource paths.'
Assert ($script:Calls[-1].environment.TRADERVI_CODE_VERSION -eq $selected.sourceCommit) 'Stage receives selected build provenance, never current Git HEAD.'
Assert ($script:Calls[-1].environment.TRADERVI_WORKING_TREE_STATE -eq 'Dirty') 'Stage retains the compiled working-tree state.'
Assert ($script:Calls[-1].environment.TRADERVI_PREPARATION_DATE -eq '2026-10-29') 'Stage receives explicit preparation date.'

# Real Windows file sharing protects the existing files throughout execution.
$hermesEntry = Join-Path $fixture $config.stages[0].entryAssembly
$dataAuditEntry = Join-Path $fixture $config.stages[1].entryAssembly
$held = $null
try {
    $held = Open-NightlyOutputLocks $config $fixture
    Assert ($held.Streams.Count -eq 12) 'All selected files are held read-only.'
    Assert-Throws { [IO.File]::AppendAllText($hermesEntry, 'bad write') } 'used by another process|being used|sharing'
    Assert ((Get-NightlyExistingOutputs $config $fixture).artifacts.Count -eq 4) 'Verification can read while locks are held.'
}
finally { Close-NightlyOutputLocks $held }
[IO.File]::WriteAllText($hermesEntry, 'synthetic assembly')
Assert ($true) 'Locks release after the run; intentional future builds are possible.'
Remove-Item -LiteralPath $dataAuditEntry
Assert-Throws { Open-NightlyOutputLocks $config $fixture } 'missing'
[IO.File]::WriteAllText($hermesEntry, 'synthetic assembly')
Assert ($true) 'Partial lock acquisition releases earlier handles on failure.'
[IO.File]::WriteAllText($dataAuditEntry, 'synthetic assembly')

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
    $pipeline.outputs = $status.outputs
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
    $pipeline.outputs = $status.outputs
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
    $pipeline = New-TestStatus; $pipeline.outputs = $status.outputs
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
$legacy = Save-NightlyDataAuditReport (AuditJson @($errorFinding) 2 1) 2
$newRules = Save-NightlyDataAuditReport (AuditJson) 0
Assert ($newRules.Comparison.State -eq 'Unavailable' -and $newRules.Comparison.ResolvedFindings.Count -eq 0) 'Changed audit rules cannot claim legacy findings were repaired.'
Assert ($newRules.Comparison.Message -match 'versions differ') 'Version transition is explained explicitly.'
$nextRules = Save-NightlyDataAuditReport (AuditJson) 0
Assert ($nextRules.Comparison.State -eq 'Compared') 'Comparisons resume after the first current-version baseline.'
Assert-Throws { ConvertFrom-DataAuditReport (AuditJson @() 2 3) } 'Unsupported DataAudit report schema'
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
[IO.File]::AppendAllText((Join-Path $fixture $config.stages[0].entryAssembly), 'tampered')
$beforeCalls = $script:Calls.Count
Assert-Throws { Invoke-NightlyStage $config.stages[0] $fixture 'synthetic-dotnet' $built.Hermes $status } 'changed after preflight'
Assert ($script:Calls.Count -eq $beforeCalls) 'Artifact tampering cannot launch a process.'
[IO.File]::WriteAllText($hermesEntry, 'synthetic assembly')
# Pinned outputs fail closed before the first operational stage, without a fallback build.
$beforeCalls = $script:Calls.Count
[IO.File]::AppendAllText($dataAuditEntry, 'modified')
Assert-Throws { Get-NightlyExistingOutputs $config $fixture } 'changed or are incomplete'
Assert ($script:Calls.Count -eq $beforeCalls) 'Changed later-stage outputs launch nothing.'
[IO.File]::WriteAllText($dataAuditEntry, 'synthetic assembly')
$extraFile = Join-Path (Split-Path -Parent $hermesEntry) 'unexpected.dll'
[IO.File]::WriteAllText($extraFile, 'added')
Assert-Throws { Get-NightlyExistingOutputs $config $fixture } 'changed or are incomplete'
Remove-Item -LiteralPath $extraFile
$runtimeConfig = Join-Path (Split-Path -Parent $hermesEntry) 'Hermes.runtimeconfig.json'
Remove-Item -LiteralPath $runtimeConfig
Assert-Throws { Open-NightlyOutputLocks $config $fixture } 'missing'
[IO.File]::WriteAllText($runtimeConfig, '{}')
$manifest.sourceCommit='not-a-commit'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5))
Assert-Throws { Get-NightlyExistingOutputs $config $fixture } 'provenance'
$manifest.sourceCommit=('1' * 40)
$manifest.stages[0].entryAssembly='other/Hermes.dll'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5))
Assert-Throws { Get-NightlyExistingOutputs $config $fixture } 'does not match'
$manifest.stages[0].entryAssembly=$config.stages[0].entryAssembly
$manifest.stages[1].name='Hermes'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5))
Assert-Throws { Get-NightlyExistingOutputs $config $fixture } 'exactly once'
$manifest.stages[1].name='DataAudit'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5))
Assert ((Get-NightlyExistingOutputs $config $fixture).artifacts.Count -eq 4) 'Unchanged reviewed outputs remain reusable after all fixtures.'
# Exercise the real script entry point only against inert fixture files. A failed
# manifest must stop before any application, and ValidateOnly must not write status.
$fixtureOperations = Join-Path $fixture 'Operations'
[void][IO.Directory]::CreateDirectory($fixtureOperations)
$fixtureConfig = Join-Path $fixtureOperations 'nightly-runner.json'
[IO.File]::WriteAllText($fixtureConfig, ($config | ConvertTo-Json -Depth 8))
$fixtureState = Join-Path $fixture 'entrypoint-state'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-TraderVINightly.ps1') -ConfigurationPath $fixtureConfig -StateDirectory $fixtureState -ValidateOnly *> (Join-Path $fixture 'validate-entrypoint.log')
Assert ($LASTEXITCODE -eq 0) 'Real ValidateOnly entry point accepts pinned fixture outputs.'
Assert (-not (Test-Path -LiteralPath $fixtureState)) 'ValidateOnly does not create operational state or log directories.'
$manifest.sourceCommit='invalid'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5))
$savedErrorPreference = $ErrorActionPreference
try {
    # Windows PowerShell turns expected native stderr into error records even when redirected.
    $ErrorActionPreference = 'Continue'
    & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-TraderVINightly.ps1') -ConfigurationPath $fixtureConfig -StateDirectory $fixtureState *> (Join-Path $fixture 'failed-entrypoint.log')
    $failedEntryExit = $LASTEXITCODE
}
finally { $ErrorActionPreference = $savedErrorPreference }
Assert ($failedEntryExit -eq 1) 'Real runner fails closed on invalid selected build provenance.'
$failedStatus = Get-Content -LiteralPath (Join-Path $fixtureState 'status.json') -Raw | ConvertFrom-Json
Assert ($failedStatus.state -eq 'Failed' -and $failedStatus.outputs.state -eq 'Failed') 'Preflight failure is saved truthfully.'
Assert ($failedStatus.stages.Count -eq 0) 'No stage begins after a preflight failure.'
[IO.File]::WriteAllText($hermesEntry, 'synthetic assembly')
Assert ($true) 'Real entry point releases output handles on failure.'

# Successful Release registrations supersede the seed independently, without copying
# outputs. Mock only compiled version reading; all hashing, locks and receipt IO are real.
$manifest.sourceCommit=('1' * 40)
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5))
$registry = Join-Path $fixture $config.outputRegistry
Assert-Throws { Get-NightlyCompiledIdentity $hermesEntry } 'Compiled source commit is unavailable'
$identityReader = (Get-Item Function:Get-NightlyCompiledIdentity).ScriptBlock
function Get-NightlyCompiledIdentity($EntryAssembly) {
    [pscustomobject]@{sourceCommit=('2' * 40);informationalVersion=('1.0.0+' + ('2' * 40))}
}
try {
    $nextHermes = Join-Path $fixture 'ordinary Release/Hermes/Hermes.dll'
    [void] [IO.Directory]::CreateDirectory((Split-Path -Parent $nextHermes))
    [IO.File]::WriteAllText($nextHermes, 'newer synthetic Release')
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $nextHermes) 'Hermes.deps.json'), '{}')
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $nextHermes) 'Hermes.runtimeconfig.json'), '{}')
    $beforeRegistration = Get-ArtifactSnapshot $fixture $nextHermes
    [string[]] $originalHashRecords = @(Get-NightlyOutputFiles (Split-Path -Parent $nextHermes) | ForEach-Object {
        $relative = Get-RepoRelativePath (Split-Path -Parent $nextHermes) $_.FullName
        "$relative`t$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
    })
    [Array]::Sort($originalHashRecords, [StringComparer]::Ordinal)
    Assert ($beforeRegistration.sha256 -eq (Get-Sha256ForText ($originalHashRecords -join "`n"))) 'Streamed hashing preserves the existing pinned directory fingerprint format.'
    & {
        # A PowerShell 7 build can launch Windows PowerShell without its
        # Get-FileHash command available. Fingerprinting must still hash the bytes.
        function Get-FileHash { throw 'Get-FileHash is unavailable in this child environment.' }
        $withoutFileHash = Get-ArtifactSnapshot $fixture $nextHermes
        Assert ($withoutFileHash.sha256 -eq $beforeRegistration.sha256) 'Output fingerprints do not depend on Get-FileHash command discovery.'
        Assert ($withoutFileHash.fileCount -eq $beforeRegistration.fileCount) 'Hashing without Get-FileHash preserves the complete file inventory.'
    }
    $receipt = Register-NightlyBuildOutput 'Hermes' $nextHermes $fixture $registry
    Assert ($receipt.workingTreeState -eq 'Unknown') 'Registration never infers compiled working-tree state from the current checkout.'
    Assert ($receipt.sha256 -eq $beforeRegistration.sha256) 'Registration reads existing binaries without changing them.'
    Assert ((Get-ArtifactSnapshot $fixture $nextHermes).sha256 -eq $beforeRegistration.sha256) 'Registration writes no files into the output directory.'
    Assert (@(Get-ChildItem -LiteralPath $registry -File).Count -eq 2) 'One receipt and one reusable lock are the only registration files.'
    $frozen = Resolve-NightlyOutputSelection $config $fixture
    $latest = Get-NightlyExistingOutputs $config $fixture $frozen
    Assert ($latest.artifacts.Hermes.entryAssembly -eq $receipt.entryAssembly) 'The next run automatically chooses the latest successful Hermes Release.'
    Assert ($latest.artifacts.DataAudit.entryAssembly -eq $config.stages[1].entryAssembly) 'Other apps retain their last known successful outputs independently.'
    Assert ($latest.sourceCommit -eq 'Mixed') 'Mixed build revisions are not mislabeled as one common revision.'
    $status = New-TestStatus
    [void] (Invoke-NightlyStage $config.stages[0] $fixture 'synthetic-dotnet' $latest.artifacts.Hermes $status)
    Assert ($script:Calls[-1].arguments[0] -eq $nextHermes) 'Execution uses the registered path, not the seed config path.'
    Assert ($script:Calls[-1].environment.TRADERVI_CODE_VERSION -eq ('2' * 40)) 'Each app receives its own compiled revision.'
    Assert ($script:Calls[-1].environment.TRADERVI_WORKING_TREE_STATE -eq 'Unknown') 'Unknown compiled working-tree state remains honest.'
    Assert ($status.stages[0].sourceCommit -eq ('2' * 40)) 'Per-app provenance is saved in stage status.'
    $held = Open-NightlyOutputLocks $frozen $fixture
    try {
        Assert-Throws { [IO.File]::AppendAllText($nextHermes, 'overwrite') } 'used by another process|being used|sharing'
        # A newer successful build in another folder can register during a run. It
        # affects the next selection, never the already frozen run's paths/identity.
        $later = Register-NightlyBuildOutput 'Hermes' $hermesEntry $fixture $registry
        Assert ((Get-NightlyExistingOutputs $config $fixture $frozen).artifacts.Hermes.entryAssembly -eq $receipt.entryAssembly) 'An active run keeps its original selection after a newer registration.'
        Assert ((Get-NightlyExistingOutputs $config $fixture).artifacts.Hermes.entryAssembly -eq $later.entryAssembly) 'The following run sees the newer successful registration automatically.'
    }
    finally { Close-NightlyOutputLocks $held }
    Assert (@(Get-ChildItem -LiteralPath $registry -File).Count -eq 2) 'Repeated successful builds replace metadata without accumulating receipts.'
    $receiptPath = Join-Path $registry 'Hermes.json'
    $oldReceiptHash = (Get-FileHash -LiteralPath $receiptPath).Hash
    $missingDependency = Join-Path (Split-Path -Parent $nextHermes) 'Hermes.deps.json'
    Remove-Item -LiteralPath $missingDependency
    Assert-Throws { Register-NightlyBuildOutput 'Hermes' $nextHermes $fixture $registry } 'missing'
    Assert ((Get-FileHash -LiteralPath $receiptPath).Hash -eq $oldReceiptHash) 'Incomplete outputs cannot replace the last successful receipt.'
    [IO.File]::WriteAllText($missingDependency, '{}')
    Assert-Throws { Register-NightlyBuildOutput 'TraderVI.Engine' $nextHermes $fixture $registry } 'Only the four'
    Assert-Throws { Register-NightlyBuildOutput 'Hermes' $nextHermes $fixture (Split-Path -Parent $nextHermes) } 'outside the compiled'
    Assert-Throws { Register-NightlyBuildOutput 'Hermes' '../Hermes.dll' $fixture $registry } 'escapes'
    # A failed overwrite of the last successful directory invalidates its hash;
    # the runner must not fall back to older files or start any operational stage.
    [IO.File]::AppendAllText($hermesEntry, 'partial failed rebuild')
    Assert-Throws { Get-NightlyExistingOutputs $config $fixture } 'changed or are incomplete'
    $savedErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-TraderVINightly.ps1') -ConfigurationPath $fixtureConfig -StateDirectory $fixtureState *> (Join-Path $fixture 'failed-registration-entrypoint.log')
        $failedRegistrationExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $savedErrorPreference }
    $failedStatus = Get-Content -LiteralPath (Join-Path $fixtureState 'status.json') -Raw | ConvertFrom-Json
    Assert ($failedRegistrationExit -eq 1 -and $failedStatus.stages.Count -eq 0) 'Real entry point rejects changed registered files before launching any stage.'
    [IO.File]::WriteAllText($hermesEntry, 'synthetic assembly')
    $invalidReceipt = Read-NightlyBuildReceiptText $receiptPath | ConvertFrom-Json
    $invalidReceipt.configuration = 'Debug'
    [IO.File]::WriteAllText($receiptPath, ($invalidReceipt | ConvertTo-Json -Depth 5))
    Assert-Throws { Resolve-NightlyOutputSelection $config $fixture } 'Invalid successful Release'
    [IO.File]::WriteAllText($receiptPath, ($later | ConvertTo-Json -Depth 5))
    & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Invoke-TraderVINightly.ps1') -ConfigurationPath $fixtureConfig -StateDirectory (Join-Path $fixture 'registered-validation-state') -ValidateOnly *> (Join-Path $fixture 'registered-validation.log')
    Assert ($LASTEXITCODE -eq 0) 'Real ValidateOnly entry point accepts an automatically registered selection.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $fixture 'registered-validation-state'))) 'Registered ValidateOnly still creates no operational state.'
}
finally { Set-Item Function:Get-NightlyCompiledIdentity -Value $identityReader }

# Exercise the real MSBuild hook using an SDK-free inert project: its Build target
# only emits a message or fails. No compiler, restore, app or application binary is involved.
$hookDirectory = Join-Path $fixture 'MSBuild hook with spaces'
[void] [IO.Directory]::CreateDirectory($hookDirectory)
$hookStub = Join-Path $hookDirectory 'registration stub.ps1'
$hookProject = Join-Path $hookDirectory 'Hermes.proj'
[IO.File]::WriteAllText($hookStub, @'
param([string] $ApplicationName, [string] $EntryAssembly)
if ($ApplicationName -ne 'Hermes' -or $EntryAssembly -ne (Join-Path $PSScriptRoot 'not a binary.dll')) { exit 12 }
[IO.File]::AppendAllText((Join-Path $PSScriptRoot 'hook-calls.txt'), "registered`n")
'@)
$targetImport = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'NightlyReleaseRegistration.targets'))
$escapedStub = [Security.SecurityElement]::Escape($hookStub)
$inertPath = [Security.SecurityElement]::Escape((Join-Path $hookDirectory 'not a binary.dll'))
[IO.File]::WriteAllText($hookProject, @"
<Project>
  <PropertyGroup><NightlyRegistrationScript>$escapedStub</NightlyRegistrationScript><TargetPath>$inertPath</TargetPath></PropertyGroup>
  <Target Name="Build"><Message Text="Inert hook fixture; nothing compiled." Importance="high" /><Error Condition="'`$(FixtureFail)' == 'true'" Text="Intentional failed build fixture." /></Target>
  <Import Project="$targetImport" />
</Project>
"@)
$hookCalls = Join-Path $hookDirectory 'hook-calls.txt'
$dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
foreach ($scenario in @(
    @{name='debug'; properties=@('-p:Configuration=Debug'); exit=0; calls=0},
    @{name='failed'; properties=@('-p:Configuration=Release','-p:FixtureFail=true'); exit=1; calls=0},
    @{name='design-time'; properties=@('-p:Configuration=Release','-p:DesignTimeBuild=true'); exit=0; calls=0},
    @{name='linux'; properties=@('-p:Configuration=Release','-p:RuntimeIdentifier=linux-x64'); exit=0; calls=0},
    @{name='release'; properties=@('-p:Configuration=Release'); exit=0; calls=1},
    @{name='release-again'; properties=@('-p:Configuration=Release'); exit=0; calls=2}
)) {
    $properties = $scenario.properties
    & $dotnet msbuild $hookProject -nologo -verbosity:minimal -target:Build @properties *> (Join-Path $hookDirectory "$($scenario.name).log")
    Assert ($LASTEXITCODE -eq $scenario.exit) "Inert $($scenario.name) build has the expected result."
    $callCount = if (Test-Path -LiteralPath $hookCalls) { @(Get-Content -LiteralPath $hookCalls).Count } else { 0 }
    Assert ($callCount -eq $scenario.calls) "Only successful runnable Release builds register: $($scenario.name)."
}
Write-Host "Passed $script:Assertions nightly regression assertions, including inert MSBuild hook fixtures. No TraderVI application was built, copied or launched; no external services ran."
