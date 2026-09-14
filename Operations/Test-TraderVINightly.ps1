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
    return Result
}
$status = New-TestStatus
$built = Invoke-LatestSourceBuild $config $fixture 'synthetic-dotnet' $status
Assert ($status.build.state -eq 'Succeeded' -and $built.Count -eq 3) 'All three isolated builds succeed despite ongoing edits.'
Assert ($status.build.environmentFiles.Count -eq 4) 'MSBuild parent imports and SDK discovery are bounded.'
Assert ($status.build.restores.Count -eq 4) 'Exactly one transient restore retry.'
Assert (@($script:Calls | Where-Object { $_.arguments[0] -eq 'restore' -and $_.arguments -notcontains '--locked-mode' }).Count -eq 0) 'Every restore is locked.'
Assert (@($script:Calls | Where-Object { $_.workingDirectory -eq $fixture }).Count -eq 0) 'Build commands never use live source directory.'
Assert ($status.stages.Count -eq 0) 'Preparation launches no operational stage.'
$stageResult = Invoke-NightlyStage $config.stages[0] $status.build.sourceDirectory 'synthetic-dotnet' $built.Hermes $status
Assert ($stageResult.state -eq 'Succeeded') 'Verified artifact starts once.'
Assert ($script:Calls[-1].environment.TRADERVI_CODE_VERSION -eq $status.build.sourceBefore.gitCommit) 'Stage receives captured commit provenance.'
Assert ($script:Calls[-1].environment.TRADERVI_WORKING_TREE_STATE -eq 'Dirty') 'Stage receives captured working-tree state.'
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
