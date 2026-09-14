# Build preparation only. Dot-sourced by the runner and the offline regression harness.
function Get-NightlySourceManifest {
    param([string] $RepoRoot)
    $paths = @(& git -c core.quotePath=false -C $RepoRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate current source.' }
    $files = @(foreach ($relative in @($paths | Sort-Object -Unique)) {
        $path = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath $relative
        # A symlink/junction could import mutable files from outside the recorded source.
        $cursor = $path
        while ($cursor -and $cursor -ne $RepoRoot) {
            if ((Test-Path -LiteralPath $cursor) -and
                ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Source links are not supported: $relative"
            }
            $cursor = Split-Path -Parent $cursor
        }
        [pscustomobject]@{
            path = $relative
            sha256 = if (Test-Path -LiteralPath $path -PathType Leaf) {
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            } else { 'MISSING' }
        }
    })
    $commit = @(& git -C $RepoRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    $dirty = @(& git -C $RepoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify working-tree state.' }
    [pscustomobject]@{
        source = [pscustomobject]@{
            sha256 = Get-Sha256ForText -Text (($files | ForEach-Object { "$($_.path)`t$($_.sha256)" }) -join "`n")
            fileCount = $files.Count
            gitCommit = [string] $commit[0]
            workingTreeState = if ($dirty.Count) { 'Dirty' } else { 'Clean' }
        }
        files = $files
    }
}

function Assert-NightlyCopy {
    param([object] $Manifest, [string] $CopyRoot)
    foreach ($file in $Manifest.files) {
        $path = Resolve-ConfiguredPath -RepoRoot $CopyRoot -ConfiguredPath $file.path
        $actual = if (Test-Path -LiteralPath $path -PathType Leaf) {
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        } else { 'MISSING' }
        if ($actual -ne $file.sha256) { throw [IO.IOException]::new("Source copy changed: $($file.path)") }
    }
}

function New-NightlySourceCopy {
    param([string] $RepoRoot, [string] $RunDirectory, [int] $MaxAttempts, [int] $RetryDelaySeconds)
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        # Never reuse or erase an earlier attempt, including one interrupted during copying.
        $copyRoot = Join-Path $RunDirectory "source-$attempt"
        if (Test-Path -LiteralPath $copyRoot) { throw "Source copy already exists: $copyRoot" }
        try {
            $before = Get-NightlySourceManifest -RepoRoot $RepoRoot
            [void] [IO.Directory]::CreateDirectory($copyRoot)
            foreach ($file in $before.files) {
                if ($file.sha256 -eq 'MISSING') { continue }
                $source = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath $file.path
                $target = Resolve-ConfiguredPath -RepoRoot $copyRoot -ConfiguredPath $file.path
                [void] [IO.Directory]::CreateDirectory((Split-Path -Parent $target))
                [IO.File]::Copy($source, $target, $false)
            }
            Assert-NightlyCopy -Manifest $before -CopyRoot $copyRoot
            $after = Get-NightlySourceManifest -RepoRoot $RepoRoot
            if ($before.source.sha256 -ne $after.source.sha256 -or
                $before.source.gitCommit -ne $after.source.gitCommit -or
                $before.source.workingTreeState -ne $after.source.workingTreeState) {
                throw [IO.IOException]::new('Source changed during capture.')
            }
            Write-JsonAtomically -Value $before -Path (Join-Path $RunDirectory 'source-manifest.json')
            return [pscustomobject]@{ root = $copyRoot; manifest = $before; attempts = $attempt }
        }
        catch [IO.IOException] {
            Write-RunLog "Source capture attempt $attempt failed: $($_.Exception.Message)"
            if ($attempt -eq $MaxAttempts) { throw }
            Start-Sleep -Seconds $RetryDelaySeconds
        }
    }
}

function Test-TransientRestoreFailure {
    param([object] $Result)
    if (-not $Result.Completed) { return $true }
    $output = "$($Result.StandardOutput)`n$($Result.StandardError)"
    # A changed lock, invalid package/version, compiler error or content-hash mismatch needs review.
    if ($output -match 'NU1004|NU110[0-9]|NU1403|NU160[0-9]') { return $false }
    return $output -match 'NU1301|NU1801|429|502|503|504|timed out|temporarily unavailable|connection.*(reset|closed)'
}

function Assert-NightlyDependencyLocks {
    param([string] $ProjectPath, [string] $CopyRoot, [hashtable] $Visited)
    if ($Visited.ContainsKey($ProjectPath)) { return }
    $Visited[$ProjectPath] = $true
    $lockPath = Join-Path (Split-Path -Parent $ProjectPath) 'packages.lock.json'
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "Dependency lock missing for $ProjectPath. Generate and review packages.lock.json before running."
    }
    [xml] $project = Get-Content -LiteralPath $ProjectPath -Raw
    foreach ($reference in $project.SelectNodes('//ProjectReference')) {
        $referencedPath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ProjectPath) $reference.Include))
        $referencedPath = Resolve-ConfiguredPath -RepoRoot $CopyRoot -ConfiguredPath $referencedPath
        Assert-NightlyDependencyLocks -ProjectPath $referencedPath -CopyRoot $CopyRoot -Visited $Visited
    }
}

function Set-NightlyBuildBoundary {
    param([string] $CopyRoot, [string] $SdkVersion)
    # Stop MSBuild walking up into the live repository for imports added after capture.
    $generated = @{}
    foreach ($name in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
        $path = Join-Path $CopyRoot $name
        if (-not (Test-Path -LiteralPath $path)) {
            [IO.File]::WriteAllText($path, '<Project />')
            $generated[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
    $globalPath = Join-Path $CopyRoot 'global.json'
    if (-not (Test-Path -LiteralPath $globalPath)) {
        Write-JsonAtomically -Value @{ sdk=@{ version=$SdkVersion; rollForward='disable' } } -Path $globalPath
        $generated['global.json'] = (Get-FileHash -LiteralPath $globalPath -Algorithm SHA256).Hash
    }
    return $generated
}

function Invoke-LatestSourceBuild {
    param([object] $Configuration, [string] $RepoRoot, [string] $DotnetPath, [object] $Status)
    $started = [DateTimeOffset]::UtcNow
    $deadline = $started.AddMinutes([int] $Configuration.build.timeoutMinutes)
    $runDirectory = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath "artifacts/nightly/$($Status.runId)"
    if (Test-Path -LiteralPath $runDirectory) { throw 'Nightly run directory already exists.' }
    [void] [IO.Directory]::CreateDirectory($runDirectory)
    $Status.build.state = 'Running'
    $Status.build.startedUtc = $started.ToString('O')
    $Status.build.runDirectory = $runDirectory
    $Status.build.message = 'Capturing current source into a private build directory.'
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    $copy = New-NightlySourceCopy -RepoRoot $RepoRoot -RunDirectory $runDirectory `
        -MaxAttempts $Configuration.build.captureAttempts -RetryDelaySeconds $Configuration.build.retryDelaySeconds
    $Status.build.sourceBefore = $copy.manifest.source
    $Status.build.sourceAfter = $copy.manifest.source
    $Status.build.captureAttempts = $copy.attempts
    $Status.build.sourceDirectory = $copy.root
    Write-RunLog "Captured latest source $($copy.manifest.source.sha256) ($($copy.manifest.source.workingTreeState)); later edits belong to the next run."
    $packageDirectory = Resolve-ConfiguredPath -RepoRoot $RepoRoot -ConfiguredPath 'artifacts/nightly/packages'
    $nugetConfig = Join-Path $copy.root 'Operations/nightly-nuget.config'
    $sdk = Invoke-CapturedProcess -FileName $DotnetPath -Arguments @('--version') -WorkingDirectory $copy.root -TimeoutMinutes 1 -Label 'SDK identity'
    if (-not $sdk.Completed -or $sdk.ExitCode -ne 0) { throw 'Cannot identify the .NET SDK.' }
    $Status.build.sdkVersion = $sdk.StandardOutput.Trim()
    $environmentFiles = Set-NightlyBuildBoundary -CopyRoot $copy.root -SdkVersion $Status.build.sdkVersion
    $Status.build | Add-Member -NotePropertyName environmentFiles -NotePropertyValue $environmentFiles -Force
    $Status.build.packageDirectory = $packageDirectory
    $visited = @{}
    foreach ($stage in $Configuration.stages) {
        $projectPath = Resolve-ConfiguredPath -RepoRoot $copy.root -ConfiguredPath $stage.project
        Assert-NightlyDependencyLocks -ProjectPath $projectPath -CopyRoot $copy.root -Visited $visited
    }
    foreach ($stage in $Configuration.stages) {
        $projectPath = Resolve-ConfiguredPath -RepoRoot $copy.root -ConfiguredPath $stage.project
        for ($attempt = 1; $attempt -le [int] $Configuration.build.restoreAttempts; $attempt++) {
            $remaining = [int] [Math]::Floor(($deadline - [DateTimeOffset]::UtcNow).TotalMinutes)
            if ($remaining -lt 1) { throw 'Build preparation time limit exceeded before restore.' }
            Write-RunLog "Restoring locked dependencies for '$($stage.name)' (attempt $attempt)."
            $result = Invoke-CapturedProcess -FileName $DotnetPath -WorkingDirectory $copy.root `
                -TimeoutMinutes ([Math]::Min($remaining, [int] $Configuration.build.restoreTimeoutMinutes)) `
                -Label "restore for '$($stage.name)'" `
                -Arguments @('restore', $projectPath, '--locked-mode', '--configfile', $nugetConfig,
                    '--packages', $packageDirectory, '--verbosity', 'minimal', '-p:NuGetAudit=false', '-nr:false')
            $Status.build.restores += [pscustomobject]@{ stage=$stage.name; attempt=$attempt; exitCode=$result.ExitCode }
            Write-JsonAtomically -Value $Status -Path $script:StatusPath
            if ($result.Completed -and $result.ExitCode -eq 0) { break }
            if ($attempt -eq [int] $Configuration.build.restoreAttempts -or -not (Test-TransientRestoreFailure $result)) {
                throw "Locked dependency restore failed for '$($stage.name)' (exit $($result.ExitCode)); no operational stage started."
            }
            Start-Sleep -Seconds ([int] $Configuration.build.retryDelaySeconds)
        }
        $remaining = [int] [Math]::Floor(($deadline - [DateTimeOffset]::UtcNow).TotalMinutes)
        if ($remaining -lt 1) { throw 'Build preparation time limit exceeded before compilation.' }
        Write-RunLog "Building isolated current source for '$($stage.name)'."
        $result = Invoke-CapturedProcess -FileName $DotnetPath -WorkingDirectory $copy.root -TimeoutMinutes $remaining `
            -Label "build for '$($stage.name)'" `
            -Arguments @('build', $projectPath, '--configuration', 'Release', '--no-restore', '--no-incremental',
                '--nologo', '--verbosity', 'minimal', '-p:UseSharedCompilation=false', '-nr:false')
        $Status.build.projects += [pscustomobject]@{
            stage=$stage.name; project=$stage.project; exitCode=$result.ExitCode
            state=if ($result.Completed -and $result.ExitCode -eq 0) { 'Succeeded' } else { 'Failed' }
        }
        Write-JsonAtomically -Value $Status -Path $script:StatusPath
        if (-not $result.Completed -or $result.ExitCode -ne 0) {
            throw "Release build failed for '$($stage.name)' (exit $($result.ExitCode)); no operational stage started."
        }
    }
    Assert-NightlyCopy -Manifest $copy.manifest -CopyRoot $copy.root
    foreach ($name in $environmentFiles.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $copy.root $name) -Algorithm SHA256).Hash -ne $environmentFiles[$name]) {
            throw "Generated build boundary changed: $name"
        }
    }
    $artifacts = @{}
    foreach ($stage in $Configuration.stages) {
        $entry = Resolve-ConfiguredPath -RepoRoot $copy.root -ConfiguredPath $stage.entryAssembly
        $snapshot = Get-ArtifactSnapshot -RepoRoot $copy.root -EntryAssembly $entry
        $artifacts[[string] $stage.name] = $snapshot
        $Status.build.artifacts += [pscustomobject]@{
            stage=$stage.name; path=$snapshot.path; sha256=$snapshot.sha256; fileCount=$snapshot.fileCount
        }
    }
    $Status.build.state = 'Succeeded'
    $Status.build.completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $Status.build.durationSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $started).TotalSeconds, 3)
    $Status.build.message = 'Latest source captured, locked dependencies restored, and isolated Release artifacts verified.'
    Write-JsonAtomically -Value $Status -Path $script:StatusPath
    Write-RunLog $Status.build.message
    return $artifacts
}
