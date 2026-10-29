[CmdletBinding()]
param(
    [string] $ConfigurationPath,

    [string] $StateDirectory,

    [switch] $Force,

    [switch] $ValidateOnly,

    [switch] $BuildOnly
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

function Write-RunLog {
    param([Parameter(Mandatory = $true)] [string] $Message)

    $timestamp = [DateTimeOffset]::UtcNow.ToString('O')
    $line = "[$timestamp] $Message"
    Write-Host $line
    [System.IO.File]::AppendAllText($script:LogPath, "$line$([Environment]::NewLine)", (New-Object System.Text.UTF8Encoding($false)))
}

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory = $true)] [string] $BasePath,
        [Parameter(Mandatory = $true)] [string] $TargetPath
    )

    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $targetFullPath = [System.IO.Path]::GetFullPath($TargetPath)
    $baseUri = New-Object System.Uri($baseFullPath)
    $targetUri = New-Object System.Uri($targetFullPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Resolve-ConfiguredPath {
    param(
        [Parameter(Mandatory = $true)] [string] $RepoRoot,
        [Parameter(Mandatory = $true)] [string] $ConfiguredPath
    )

    $resolved = if ([System.IO.Path]::IsPathRooted($ConfiguredPath)) {
        [System.IO.Path]::GetFullPath($ConfiguredPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $ConfiguredPath))
    }

    $rootPrefix = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Configured path escapes the repository: $ConfiguredPath"
    }

    return $resolved
}

function Get-Sha256ForText {
    param([Parameter(Mandatory = $true)] [string] $Text)

    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($Text)
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($hasher.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-ArtifactSnapshot {
    param(
        [Parameter(Mandatory = $true)] [string] $RepoRoot,
        [Parameter(Mandatory = $true)] [string] $EntryAssembly
    )

    if (-not (Test-Path -LiteralPath $EntryAssembly -PathType Leaf)) {
        throw "Built entry assembly is missing: $EntryAssembly"
    }

    $artifactDirectory = Split-Path -Parent $EntryAssembly
    $records = New-Object System.Collections.Generic.List[string]
    foreach ($file in @(Get-ChildItem -LiteralPath $artifactDirectory -Recurse -File | Sort-Object FullName)) {
        $relativePath = Get-RepoRelativePath -BasePath $artifactDirectory -TargetPath $file.FullName
        $records.Add("$relativePath`t$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)")
    }

    return [pscustomobject] [ordered]@{
        path = Get-RepoRelativePath -BasePath $RepoRoot -TargetPath $artifactDirectory
        sha256 = Get-Sha256ForText -Text ($records -join "`n")
        fileCount = $records.Count
    }
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
            # Windows PowerShell lacks Process.Kill(entireProcessTree). Stop compiler children too.
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

    if ($Configuration.schemaVersion -ne 3) {
        throw "Unsupported nightly-runner configuration schema '$($Configuration.schemaVersion)'."
    }
    if ([string] $Configuration.build.configuration -ne 'Release') {
        throw 'The unattended runner requires the Release build configuration.'
    }
    if (-not [bool] $Configuration.build.restoreDependencies) {
        throw 'The isolated nightly build requires locked dependency restore.'
    }
    if ([int] $Configuration.build.timeoutMinutes -le 0) {
        throw 'The build timeout must be positive.'
    }
    foreach ($setting in @('captureAttempts', 'restoreAttempts')) {
        if ([int] $Configuration.build.$setting -lt 1 -or [int] $Configuration.build.$setting -gt 3) {
            throw "$setting must be between 1 and 3."
        }
    }
    if ([int] $Configuration.build.retryDelaySeconds -lt 0 -or [int] $Configuration.build.retryDelaySeconds -gt 60 -or
        [int] $Configuration.build.restoreTimeoutMinutes -lt 1) {
        throw 'Invalid preparation retry delay or restore timeout.'
    }
    if (@($Configuration.stages).Count -eq 0) {
        throw 'The nightly configuration contains no stages.'
    }

    foreach ($stage in $Configuration.stages) {
        $projectPath = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath ([string] $stage.project)
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "Stage '$($stage.name)' project does not exist: $projectPath"
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

    $entryAssembly = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath ([string] $Stage.entryAssembly)
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
        message = $null
    }
    $Status.stages += $stageStatus
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    Write-RunLog "Starting stage '$($Stage.name)' from the current-source build '$entryAssembly'."

    $stageArguments = @($entryAssembly)
    if ($Stage.PSObject.Properties.Name -contains 'arguments') { $stageArguments += @($Stage.arguments) }
    $result = Invoke-CapturedProcess `
        -FileName $DotnetPath `
        -Arguments $stageArguments `
        -WorkingDirectory $RepoRoot `
        -TimeoutMinutes ([int] $Stage.timeoutMinutes) `
        -Label "stage '$($Stage.name)'" `
        -EnvironmentOverrides @{
            TRADERVI_CODE_VERSION = $Status.build.sourceBefore.gitCommit
            TRADERVI_WORKING_TREE_STATE = $Status.build.sourceBefore.workingTreeState
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
            -RepoRoot $Status.build.sourceDirectory `
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

. (Join-Path $PSScriptRoot 'NightlyBuild.ps1')
. (Join-Path $PSScriptRoot 'NightlyDataAudit.ps1')

$resolvedConfigurationPath = (Resolve-Path -LiteralPath $ConfigurationPath).Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $resolvedConfigurationPath) '..'))
$configuration = Get-Content -LiteralPath $resolvedConfigurationPath -Raw | ConvertFrom-Json
Test-Configuration -Configuration $configuration -RepoRoot $repoRoot

$dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetPath = [System.IO.Path]::GetFullPath((Get-Command dotnet -ErrorAction Stop).Source)
}

if ($ValidateOnly) {
    Write-Host "Nightly latest-source configuration is valid: $resolvedConfigurationPath"
    exit 0
}

$stateDirectory = [System.IO.Path]::GetFullPath($StateDirectory)
if ($BuildOnly -and -not $stateDirectory.StartsWith((Join-Path $repoRoot 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'BuildOnly requires a separate StateDirectory under repository artifacts; it cannot overwrite operational status.'
}
[System.IO.Directory]::CreateDirectory($stateDirectory) | Out-Null
$logsDirectory = Join-Path $stateDirectory 'logs'
[System.IO.Directory]::CreateDirectory($logsDirectory) | Out-Null
$script:StatusPath = Join-Path $stateDirectory 'status.json'
$runId = [Guid]::NewGuid().ToString('D')
$script:LogPath = Join-Path $logsDirectory "$([DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss'))-$runId.log"
$lockPath = Join-Path $stateDirectory 'nightly.lock'
$lockStream = $null
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
        $today = [DateTimeOffset]::Now.ToString('yyyy-MM-dd')
        if ($previousStatus.localRunDate -eq $today -and $previousStatus.state -in @('Succeeded', 'Attention')) {
            Write-RunLog "A completed run already exists for local date $today (state $($previousStatus.state)). Use -Force only after reviewing the prior run."
            exit 0
        }
    }

    $status = [pscustomobject] [ordered]@{
        schemaVersion = 3
        runId = $runId
        state = 'Running'
        localRunDate = [DateTimeOffset]::Now.ToString('yyyy-MM-dd')
        startedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        completedUtc = $null
        hostName = [Environment]::MachineName
        userName = [Environment]::UserName
        repositoryRoot = $repoRoot
        configurationPath = $resolvedConfigurationPath
        logPath = $script:LogPath
        build = [pscustomobject] [ordered]@{
            state = 'Pending'
            startedUtc = $null
            completedUtc = $null
            durationSeconds = $null
            sourceBefore = $null
            sourceAfter = $null
            projects = @()
            artifacts = @()
            restores = @()
            runDirectory = $null
            sourceDirectory = $null
            packageDirectory = $null
            sdkVersion = $null
            captureAttempts = 0
            message = 'Build preflight has not started.'
        }
        stages = @()
        message = 'Nightly pipeline is running.'
    }
    Write-JsonAtomically -Value $status -Path $script:StatusPath
    Write-RunLog "TraderVI nightly run $runId started from current source."

    $artifactSnapshots = Invoke-LatestSourceBuild `
        -Configuration $configuration `
        -RepoRoot $repoRoot `
        -DotnetPath $dotnetPath `
        -Status $status

    if ($BuildOnly) {
        $status.state = 'Prepared'
        $status.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        $status.message = 'Build-only validation completed. No operational stage was started.'
        Write-JsonAtomically -Value $status -Path $script:StatusPath
        Write-RunLog $status.message
        exit 0
    }

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
        $status.message = 'The current source built successfully and all nightly stages completed successfully.'
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
                    schemaVersion = 3
                    runId = $runId
                    state = 'Failed'
                    localRunDate = [DateTimeOffset]::Now.ToString('yyyy-MM-dd')
                    startedUtc = $null
                    completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                    repositoryRoot = $repoRoot
                    configurationPath = $resolvedConfigurationPath
                    logPath = $script:LogPath
                    build = $null
                    stages = @()
                    message = $message
                }
            }
            else {
                $status.state = 'Failed'
                $status.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                $status.message = $message
                if ($null -ne $status.build -and $status.build.state -in @('Running', 'Failed')) {
                    $status.build.state = 'Failed'
                    $status.build.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
                    $status.build.message = $message
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
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
}
