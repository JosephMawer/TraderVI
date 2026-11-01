[CmdletBinding()]
param(
    [string] $ConfigurationPath,

    [string] $StateDirectory,

    [string] $PreparationDate,

    [switch] $Force,

    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $ConfigurationPath = Join-Path $PSScriptRoot 'nightly-runner.json'
}
if ([string]::IsNullOrWhiteSpace($StateDirectory)) {
    $StateDirectory = Join-Path $env:LOCALAPPDATA 'TraderVI\Nightly'
}

function Write-JsonAtomically {
    param(
        [Parameter(Mandatory = $true)] [object] $Value,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $temporaryPath = "$Path.tmp"
    $json = $Value | ConvertTo-Json -Depth 16
    [System.IO.File]::WriteAllText($temporaryPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Resolve-NightlyPreparationDate {
    param([string] $RequestedDate, [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow)
    $toronto = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
    $local = [TimeZoneInfo]::ConvertTime($Now, $toronto)
    $today = $local.Date
    if ([string]::IsNullOrEmpty($RequestedDate)) { return $today.ToString('yyyy-MM-dd') }
    $date = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($RequestedDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref] $date)) {
        throw 'Preparation date must be yyyy-MM-dd.'
    }
    if ($date -lt $today -or $date -gt $today.AddDays(1)) {
        throw 'Preparation date must be today or tomorrow in Toronto.'
    }
    if ($date -gt $today -and $local.TimeOfDay -lt [TimeSpan]::FromHours(16)) {
        throw 'Early preparation must wait until 16:00 Toronto.'
    }
    return $date.ToString('yyyy-MM-dd')
}

function Test-NightlyPreparationCompleted {
    param($PreviousStatus, [string] $Date)
    $previousDate = if ($PreviousStatus.PSObject.Properties.Name -contains 'preparationDate') {
        $PreviousStatus.preparationDate
    } else { $PreviousStatus.localRunDate }
    return $previousDate -eq $Date -and $PreviousStatus.state -in @('Succeeded', 'Attention')
}

function Write-RunLog {
    param([Parameter(Mandatory = $true)] [string] $Message)

    $timestamp = [DateTimeOffset]::UtcNow.ToString('O')
    $line = "[$timestamp] $Message"
    Write-Host $line
    [System.IO.File]::AppendAllText($script:LogPath, "$line$([Environment]::NewLine)", (New-Object System.Text.UTF8Encoding($false)))
}

function ConvertTo-ArgumentString {
    param([Parameter(Mandatory = $true)] [object[]] $Arguments)

    return (@($Arguments) | ForEach-Object {
        '"' + ([string] $_).Replace('"', '\"') + '"'
    }) -join ' '
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)] [string] $FileName,
        [Parameter(Mandatory = $true)] [object[]] $Arguments,
        [Parameter(Mandatory = $true)] [string] $WorkingDirectory,
        [Parameter(Mandatory = $true)] [int] $TimeoutMinutes,
        [Parameter(Mandatory = $true)] [string] $Label,
        [hashtable] $EnvironmentOverrides = @{}
    )

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $process.StartInfo.FileName = $FileName
    $process.StartInfo.Arguments = ConvertTo-ArgumentString -Arguments $Arguments
    $process.StartInfo.WorkingDirectory = $WorkingDirectory
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.EnvironmentVariables['DOTNET_NOLOGO'] = '1'
    $process.StartInfo.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    foreach ($key in $EnvironmentOverrides.Keys) {
        $process.StartInfo.EnvironmentVariables[$key] = [string] $EnvironmentOverrides[$key]
    }

    if (-not $process.Start()) {
        throw "Failed to start $Label."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timeoutMilliseconds = [int64] $TimeoutMinutes * 60 * 1000
    $completed = $process.WaitForExit([int] $timeoutMilliseconds)
    if (-not $completed) {
        try {
            # Windows PowerShell lacks Process.Kill(entireProcessTree). Stop this stage's children too.
            & "$env:SystemRoot\System32\taskkill.exe" /PID $process.Id /T /F 2>$null | Out-Null
            if (-not $process.WaitForExit(10000)) { throw 'Process tree did not stop.' }
        }
        catch {
            throw "Could not terminate timed-out $Label`: $($_.Exception.Message)"
        }
        $process.WaitForExit()
    }
    else {
        $process.WaitForExit()
    }

    if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]] @($stdoutTask, $stderrTask), 10000)) {
        throw "Output capture did not close for $Label."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        [System.IO.File]::AppendAllText($script:LogPath, "$stdout$([Environment]::NewLine)", (New-Object System.Text.UTF8Encoding($false)))
    }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        [System.IO.File]::AppendAllText($script:LogPath, "[stderr]$([Environment]::NewLine)$stderr$([Environment]::NewLine)", (New-Object System.Text.UTF8Encoding($false)))
    }

    return [pscustomobject]@{
        Completed = $completed
        ExitCode = if ($completed) { $process.ExitCode } else { 124 }
        StandardOutput = $stdout
        StandardError = $stderr
    }
}

function Test-Configuration {
    param(
        [Parameter(Mandatory = $true)] [object] $Configuration,
        [Parameter(Mandatory = $true)] [string] $RepoRoot
    )

    if ($Configuration.schemaVersion -ne 4 -or $Configuration.executionMode -ne 'ExistingOutputs') {
        throw 'The nightly runner requires schema 4 and ExistingOutputs mode; builds and source copies are unsupported.'
    }
    [void] (Resolve-ConfiguredPath $RepoRoot ([string] $Configuration.outputsManifest))
    if ($Configuration.PSObject.Properties.Name -contains 'outputRegistry') {
        [void] (Resolve-ConfiguredPath $RepoRoot ([string] $Configuration.outputRegistry))
    }
    if (($Configuration.stages.name -join ',') -ne 'Hermes,DataAudit,Delphi,Athena') {
        throw 'The configured stage order must be Hermes, DataAudit, Delphi, Athena.'
    }
    if (@($Configuration.stages).Count -eq 0) {
        throw 'The nightly configuration contains no stages.'
    }

    foreach ($stage in $Configuration.stages) {
        if ([IO.Path]::GetFileName([string] $stage.entryAssembly) -ne "$($stage.name).dll") {
            throw "Stage '$($stage.name)' must select its compiled DLL directly."
        }
        [void] (Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath ([string] $stage.entryAssembly))
        if ([int] $stage.timeoutMinutes -le 0) {
            throw "Stage '$($stage.name)' has an invalid timeout."
        }
        if (@($stage.successExitCodes).Count -eq 0) {
            throw "Stage '$($stage.name)' has no success exit code."
        }
    }
}

function Invoke-NightlyStage {
    param(
        [Parameter(Mandatory = $true)] [object] $Stage,
        [Parameter(Mandatory = $true)] [string] $RepoRoot,
        [Parameter(Mandatory = $true)] [string] $DotnetPath,
        [Parameter(Mandatory = $true)] [object] $ExpectedArtifact,
        [Parameter(Mandatory = $true)] [object] $Status
    )

    $entryAssembly = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath ([string] $ExpectedArtifact.entryAssembly)
    $currentArtifact = Get-ArtifactSnapshot -RepoRoot $RepoRoot -EntryAssembly $entryAssembly
    if (-not [string]::Equals([string] $ExpectedArtifact.sha256, [string] $currentArtifact.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Built artifacts for '$($Stage.name)' changed after preflight. The stage was not started."
    }

    $stageStartedUtc = [DateTimeOffset]::UtcNow
    $stageStatus = [pscustomobject] [ordered]@{
        name = [string] $Stage.name
        state = 'Running'
        startedUtc = $stageStartedUtc.ToString('O')
        completedUtc = $null
        durationSeconds = $null
        exitCode = $null
        artifactSha256 = [string] $currentArtifact.sha256
        entryAssembly = [string] $ExpectedArtifact.entryAssembly
        sourceCommit = [string] $ExpectedArtifact.sourceCommit
        workingTreeState = [string] $ExpectedArtifact.workingTreeState
        message = $null
    }
    $Status.stages += $stageStatus
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    Write-RunLog "Starting stage '$($Stage.name)' from selected existing outputs '$entryAssembly'."

    $stageArguments = @($entryAssembly)
    if ($Stage.PSObject.Properties.Name -contains 'arguments') { $stageArguments += @($Stage.arguments) }
    $result = Invoke-CapturedProcess `
        -FileName $DotnetPath `
        -Arguments $stageArguments `
        -WorkingDirectory $RepoRoot `
        -TimeoutMinutes ([int] $Stage.timeoutMinutes) `
        -Label "stage '$($Stage.name)'" `
        -EnvironmentOverrides @{
            TRADERVI_CODE_VERSION = $ExpectedArtifact.sourceCommit
            TRADERVI_WORKING_TREE_STATE = $ExpectedArtifact.workingTreeState
            TRADERVI_PREPARATION_DATE = $Status.preparationDate
        }

    $stageCompletedUtc = [DateTimeOffset]::UtcNow
    $stageStatus.completedUtc = $stageCompletedUtc.ToString('O')
    $stageStatus.durationSeconds = [Math]::Round(($stageCompletedUtc - $stageStartedUtc).TotalSeconds, 3)
    $stageStatus.exitCode = $result.ExitCode

    if (-not $result.Completed) {
        $stageStatus.state = 'Failed'
        $stageStatus.message = "Timed out after $($Stage.timeoutMinutes) minutes."
    }
    else {
        $combinedOutput = "$($result.StandardOutput)$([Environment]::NewLine)$($result.StandardError)"
        $matchedFailurePattern = @($Stage.failurePatterns) | Where-Object {
            $combinedOutput -match [string] $_
        } | Select-Object -First 1
        $matchedAttentionPattern = @($Stage.attentionPatterns) | Where-Object {
            $combinedOutput -match [string] $_
        } | Select-Object -First 1

        if ($matchedFailurePattern) {
            $stageStatus.state = 'Failed'
            $stageStatus.message = "Output matched failure pattern '$matchedFailurePattern'."
        }
        elseif (@($Stage.successExitCodes) -notcontains $result.ExitCode -and
                @($Stage.attentionExitCodes) -notcontains $result.ExitCode) {
            # A warning must never hide a later crash, failed backup or other fatal exit.
            $stageStatus.state = 'Failed'
            $stageStatus.message = "Exited with code $($result.ExitCode)."
        }
        elseif ($matchedAttentionPattern) {
            $stageStatus.state = 'Attention'
            $stageStatus.message = "Output matched attention pattern '$matchedAttentionPattern'."
        }
        elseif (@($Stage.successExitCodes) -contains $result.ExitCode) {
            $stageStatus.state = 'Succeeded'
            $stageStatus.message = 'Completed successfully.'
        }
        elseif (@($Stage.attentionExitCodes) -contains $result.ExitCode) {
            $stageStatus.state = 'Attention'
            $stageStatus.message = "Completed with attention exit code $($result.ExitCode)."
        }
        else {
            $stageStatus.state = 'Failed'
            $stageStatus.message = "Exited with code $($result.ExitCode)."
        }
    }

    if ($Stage.name -eq 'DataAudit' -and $stageStatus.state -ne 'Failed') {
        try {
            $audit = Save-NightlyDataAuditReport -Json $result.StandardOutput -ExitCode $result.ExitCode
            $stageStatus | Add-Member -NotePropertyName dataAudit -NotePropertyValue $audit
            $stageStatus.message = "$($audit.Run.Report.ErrorCount) errors; $($audit.Run.Report.WarningCount) warnings. $($audit.Comparison.Message)"
        }
        catch {
            $stageStatus.state = 'Failed'
            $stageStatus.message = 'Audit report could not be validated or saved; review the run log. No clean audit is claimed.'
            Write-RunLog "DataAudit reporting failed: $($_.Exception.Message)"
        }
    }
    $Status.stages[-1] = $stageStatus
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    Write-RunLog "Stage '$($Stage.name)' finished as $($stageStatus.state): $($stageStatus.message)"
    return $stageStatus
}

function Invoke-NightlyStages {
    param($Configuration, $Status, [string] $DotnetPath, $ArtifactSnapshots)
    $hasFailure = $false
    $hasAttention = $false
    foreach ($stage in $Configuration.stages) {
        $result = Invoke-NightlyStage `
            -Stage $stage `
            -RepoRoot $Status.repositoryRoot `
            -DotnetPath $DotnetPath `
            -ExpectedArtifact $ArtifactSnapshots[[string] $stage.name] `
            -Status $Status
        if ($result.state -eq 'Attention') {
            $hasAttention = $true
        }
        elseif ($result.state -eq 'Failed') {
            $hasFailure = $true
            if (-not [bool] $stage.continueAfterFailure) {
                Write-RunLog "Stopping before later stages because '$($stage.name)' failed."
                break
            }
        }
    }
    return [pscustomobject]@{ HasFailure=$hasFailure; HasAttention=$hasAttention }
}

. (Join-Path $PSScriptRoot 'NightlyOutputs.ps1')
. (Join-Path $PSScriptRoot 'NightlyDataAudit.ps1')

$resolvedConfigurationPath = (Resolve-Path -LiteralPath $ConfigurationPath).Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $resolvedConfigurationPath) '..'))
$configuration = Get-Content -LiteralPath $resolvedConfigurationPath -Raw | ConvertFrom-Json
Test-Configuration -Configuration $configuration -RepoRoot $repoRoot
$resolvedPreparationDate = Resolve-NightlyPreparationDate $PreparationDate

$dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetPath = [System.IO.Path]::GetFullPath((Get-Command dotnet -ErrorAction Stop).Source)
}

if ($ValidateOnly) {
    $validationLocks = $null
    try {
        $selection = Resolve-NightlyOutputSelection $configuration $repoRoot
        $validationLocks = Open-NightlyOutputLocks $selection $repoRoot
        $validated = Get-NightlyExistingOutputs $configuration $repoRoot $selection
        Write-Host "Existing outputs verified: $($validated.outputSetId). No build, copy, operational stage or status write."
        foreach ($stage in $selection.stages) { Write-Host "$($stage.name): $($stage.entryAssembly)" }
    }
    finally { Close-NightlyOutputLocks $validationLocks }
    exit 0
}

$stateDirectory = [System.IO.Path]::GetFullPath($StateDirectory)
[System.IO.Directory]::CreateDirectory($stateDirectory) | Out-Null
$logsDirectory = Join-Path $stateDirectory 'logs'
[System.IO.Directory]::CreateDirectory($logsDirectory) | Out-Null
$script:StatusPath = Join-Path $stateDirectory 'status.json'
$runId = [Guid]::NewGuid().ToString('D')
$script:LogPath = Join-Path $logsDirectory "$([DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))-$runId.log"
$lockPath = Join-Path $stateDirectory 'nightly.lock'
$lockStream = $null
$outputLocks = $null
$status = $null

try {
    try {
        $lockStream = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        Write-Host 'Another TraderVI nightly run holds the single-instance lock. No work was started.'
        exit 3
    }

    if (-not $Force -and (Test-Path -LiteralPath $script:StatusPath -PathType Leaf)) {
        $previousStatus = Get-Content -LiteralPath $script:StatusPath -Raw | ConvertFrom-Json
        if (Test-NightlyPreparationCompleted $previousStatus $resolvedPreparationDate) {
            Write-RunLog "A completed run already exists for preparation date $resolvedPreparationDate (state $($previousStatus.state)). Use -Force only after reviewing the prior run."
            exit 0
        }
    }

    $status = [pscustomobject] [ordered]@{
        schemaVersion = 4
        runId = $runId
        state = 'Running'
        localRunDate = [DateTimeOffset]::Now.ToString('yyyy-MM-dd')
        preparationDate = $resolvedPreparationDate
        startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        completedUtc = $null
        hostName = [Environment]::MachineName
        userName = [Environment]::UserName
        repositoryRoot = $repoRoot
        configurationPath = $resolvedConfigurationPath
        logPath = $script:LogPath
        outputs = [pscustomobject] [ordered]@{
            state = 'Pending'
            mode = 'ExistingOutputs'
            outputSetId = $null
            sourceCommit = $null
            workingTreeState = $null
            manifestSha256 = $null
            artifacts = @()
            message = 'Existing-output verification has not started.'
        }
        stages = @()
        message = 'Nightly pipeline is running.'
    }
    Write-JsonAtomically -Value $status -Path $script:StatusPath
    Write-RunLog "TraderVI nightly run $runId started from existing outputs for preparation date $resolvedPreparationDate."
    $status.outputs.state = 'Running'
    $selection = Resolve-NightlyOutputSelection $configuration $repoRoot
    $outputLocks = Open-NightlyOutputLocks $selection $repoRoot
    $selected = Get-NightlyExistingOutputs $configuration $repoRoot $selection
    $artifactSnapshots = $selected.artifacts
    $status.outputs.outputSetId = $selected.outputSetId
    $status.outputs.sourceCommit = $selected.sourceCommit
    $status.outputs.workingTreeState = $selected.workingTreeState
    $status.outputs.manifestSha256 = $selected.manifestSha256
    foreach ($stage in $configuration.stages) {
        $snapshot = $artifactSnapshots[[string] $stage.name]
        $status.outputs.artifacts += [pscustomobject]@{
            stage=$stage.name; path=$snapshot.path; sha256=$snapshot.sha256; fileCount=$snapshot.fileCount
            entryAssembly=$snapshot.entryAssembly; sourceCommit=$snapshot.sourceCommit; workingTreeState=$snapshot.workingTreeState
            registeredUtc=$snapshot.registeredUtc; receiptSha256=$snapshot.receiptSha256
        }
    }
    $status.outputs.state = 'Verified'
    $status.outputs.message = 'Selected existing outputs verified and held read-only for this run. Nothing restored, compiled or copied.'
    Write-JsonAtomically -Value $status -Path $script:StatusPath
    Write-RunLog $status.outputs.message

    $outcome = Invoke-NightlyStages $configuration $status $dotnetPath $artifactSnapshots

    $status.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    if ($outcome.HasFailure) {
        $status.state = 'Failed'
        $status.message = 'One or more stages failed. Review the referenced log; no automatic retry was attempted.'
    }
    elseif ($outcome.HasAttention) {
        $status.state = 'Attention'
        $status.message = 'All stages completed, but at least one stage reported an attention condition.'
    }
    else {
        $status.state = 'Succeeded'
        $status.message = 'Selected existing outputs verified and all nightly stages completed successfully.'
    }

    Write-JsonAtomically -Value $status -Path $script:StatusPath
    Write-RunLog "TraderVI nightly run $runId finished as $($status.state)."
    if ($status.state -eq 'Failed') { exit 1 }
    if ($status.state -eq 'Attention') { exit 2 }
    exit 0
}
catch {
    $message = $_.Exception.Message
    if ($null -ne $script:LogPath) {
        try { Write-RunLog "Nightly runner failed: $message" } catch { }
    }

    if ($null -ne $script:StatusPath) {
        try {
            if ($null -eq $status) {
                $status = [pscustomobject] [ordered]@{
                    schemaVersion = 4
                    runId = $runId
                    state = 'Failed'
                    localRunDate = [DateTimeOffset]::Now.ToString('yyyy-MM-dd')
                    preparationDate = $resolvedPreparationDate
                    startedUtc = $null
                    completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                    repositoryRoot = $repoRoot
                    configurationPath = $resolvedConfigurationPath
                    logPath = $script:LogPath
                    outputs = $null
                    stages = @()
                    message = $message
                }
            }
            else {
                $status.state = 'Failed'
                $status.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                $status.message = $message
                if ($null -ne $status.outputs -and $status.outputs.state -in @('Running', 'Failed')) {
                    $status.outputs.state = 'Failed'
                    $status.outputs.message = $message
                }
            }
            Write-JsonAtomically -Value $status -Path $script:StatusPath
        }
        catch { }
    }
    Write-Error $message
    exit 1
}
finally {
    Close-NightlyOutputLocks $outputLocks
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
}
