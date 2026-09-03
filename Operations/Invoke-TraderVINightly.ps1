[CmdletBinding()]
param(
    [string] $ConfigurationPath,

    [string] $StateDirectory,

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

function Get-SourceSnapshot {
    param([Parameter(Mandatory = $true)] [string] $RepoRoot)

    $paths = @(& git -C $RepoRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate the Git working tree for the source fingerprint.'
    }

    $records = New-Object System.Collections.Generic.List[string]
    foreach ($relativePath in @($paths | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace([string] $relativePath)) {
            continue
        }

        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot ([string] $relativePath)))
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $fileHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
            $records.Add("$relativePath`t$fileHash")
        }
        else {
            $records.Add("$relativePath`tMISSING")
        }
    }

    $commit = @(& git -C $RepoRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or @($commit).Count -eq 0) {
        $commit = @('unavailable')
    }
    $workingTree = @(& git -C $RepoRoot status --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git working-tree state.'
    }

    return [pscustomobject] [ordered]@{
        sha256 = Get-Sha256ForText -Text ($records -join "`n")
        fileCount = $records.Count
        gitCommit = [string] $commit[0]
        workingTreeState = if ($workingTree.Count -gt 0) { 'Dirty' } else { 'Clean' }
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
        [Parameter(Mandatory = $true)] [string] $Label
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

    if (-not $process.Start()) {
        throw "Failed to start $Label."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timeoutMilliseconds = [int64] $TimeoutMinutes * 60 * 1000
    $completed = $process.WaitForExit([int] $timeoutMilliseconds)
    if (-not $completed) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            Write-RunLog "Could not terminate timed-out $Label`: $($_.Exception.Message)"
        }
        $process.WaitForExit()
    }
    else {
        $process.WaitForExit()
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

    if ($Configuration.schemaVersion -ne 2) {
        throw "Unsupported nightly-runner configuration schema '$($Configuration.schemaVersion)'."
    }
    if ([string] $Configuration.build.configuration -ne 'Release') {
        throw 'The unattended runner requires the Release build configuration.'
    }
    if ([bool] $Configuration.build.restoreDependencies) {
        throw 'Unattended dependency restore is disabled. Restore reviewed dependencies separately before the nightly run.'
    }
    if ([int] $Configuration.build.timeoutMinutes -le 0) {
        throw 'The build timeout must be positive.'
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

function Invoke-LatestSourceBuild {
    param(
        [Parameter(Mandatory = $true)] [object] $Configuration,
        [Parameter(Mandatory = $true)] [string] $RepoRoot,
        [Parameter(Mandatory = $true)] [string] $DotnetPath,
        [Parameter(Mandatory = $true)] [object] $Status
    )

    $buildStartedUtc = [DateTimeOffset]::UtcNow
    $sourceBefore = Get-SourceSnapshot -RepoRoot $RepoRoot
    $Status.build.state = 'Running'
    $Status.build.startedUtc = $buildStartedUtc.ToString('O')
    $Status.build.sourceBefore = $sourceBefore
    $Status.build.message = 'Building the current Git working tree.'
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    Write-RunLog "Source fingerprint before build: $($sourceBefore.sha256) ($($sourceBefore.workingTreeState))."

    $builtProjects = @{}
    foreach ($stage in $Configuration.stages) {
        $projectPath = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath ([string] $stage.project)
        if ($builtProjects.ContainsKey($projectPath)) {
            continue
        }

        $projectStartedUtc = [DateTimeOffset]::UtcNow
        Write-RunLog "Building current source for '$($stage.name)': $projectPath"
        $result = Invoke-CapturedProcess `
            -FileName $DotnetPath `
            -Arguments @('build', $projectPath, '--configuration', 'Release', '--no-restore', '--no-incremental', '--nologo', '--verbosity', 'minimal') `
            -WorkingDirectory $RepoRoot `
            -TimeoutMinutes ([int] $Configuration.build.timeoutMinutes) `
            -Label "build for '$($stage.name)'"

        $projectCompletedUtc = [DateTimeOffset]::UtcNow
        $projectStatus = [pscustomobject] [ordered]@{
            stage = [string] $stage.name
            project = Get-RepoRelativePath -BasePath $RepoRoot -TargetPath $projectPath
            startedUtc = $projectStartedUtc.ToString('O')
            completedUtc = $projectCompletedUtc.ToString('O')
            durationSeconds = [Math]::Round(($projectCompletedUtc - $projectStartedUtc).TotalSeconds, 3)
            exitCode = $result.ExitCode
            state = if ($result.Completed -and $result.ExitCode -eq 0) { 'Succeeded' } else { 'Failed' }
        }
        $Status.build.projects += $projectStatus
        Write-JsonAtomically -Value $Status -Path $script:StatusPath
        if ($projectStatus.state -eq 'Failed') {
            $Status.build.state = 'Failed'
            $Status.build.message = if ($result.Completed) {
                "Release build failed for '$($stage.name)' with exit code $($result.ExitCode). No operational stage was started."
            }
            else {
                "Release build for '$($stage.name)' timed out. No operational stage was started."
            }
            throw $Status.build.message
        }
        $builtProjects[$projectPath] = $true
    }

    $sourceAfter = Get-SourceSnapshot -RepoRoot $RepoRoot
    $Status.build.sourceAfter = $sourceAfter
    if (-not [string]::Equals([string] $sourceBefore.sha256, [string] $sourceAfter.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        $Status.build.state = 'Failed'
        $Status.build.message = 'The source tree changed while the preflight build was running. No operational stage was started.'
        throw $Status.build.message
    }

    $artifactSnapshots = @{}
    foreach ($stage in $Configuration.stages) {
        $entryAssembly = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath ([string] $stage.entryAssembly)
        $snapshot = Get-ArtifactSnapshot -RepoRoot $RepoRoot -EntryAssembly $entryAssembly
        $artifactSnapshots[[string] $stage.name] = $snapshot
        $Status.build.artifacts += [pscustomobject] [ordered]@{
            stage = [string] $stage.name
            path = $snapshot.path
            sha256 = $snapshot.sha256
            fileCount = $snapshot.fileCount
        }
    }

    $buildCompletedUtc = [DateTimeOffset]::UtcNow
    $Status.build.state = 'Succeeded'
    $Status.build.completedUtc = $buildCompletedUtc.ToString('O')
    $Status.build.durationSeconds = [Math]::Round(($buildCompletedUtc - $buildStartedUtc).TotalSeconds, 3)
    $Status.build.message = 'Current source built successfully and remained unchanged during preflight.'
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    Write-RunLog $Status.build.message
    return $artifactSnapshots
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

    $result = Invoke-CapturedProcess `
        -FileName $DotnetPath `
        -Arguments @($entryAssembly) `
        -WorkingDirectory $RepoRoot `
        -TimeoutMinutes ([int] $Stage.timeoutMinutes) `
        -Label "stage '$($Stage.name)'"

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

    $Status.stages[-1] = $stageStatus
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    Write-RunLog "Stage '$($Stage.name)' finished as $($stageStatus.state): $($stageStatus.message)"
    return $stageStatus
}

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
        schemaVersion = 2
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

    $hasFailure = $false
    $hasAttention = $false
    foreach ($stage in $configuration.stages) {
        $result = Invoke-NightlyStage `
            -Stage $stage `
            -RepoRoot $repoRoot `
            -DotnetPath $dotnetPath `
            -ExpectedArtifact $artifactSnapshots[[string] $stage.name] `
            -Status $status
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

    $status.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    if ($hasFailure) {
        $status.state = 'Failed'
        $status.message = 'One or more stages failed. Review the referenced log; no automatic retry was attempted.'
    }
    elseif ($hasAttention) {
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
                    schemaVersion = 2
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
