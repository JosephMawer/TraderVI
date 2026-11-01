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
        throw "Compiled entry assembly is missing: $EntryAssembly"
    }

    $EntryAssembly = Assert-NightlyOutputPath $RepoRoot $EntryAssembly
    $artifactDirectory = Split-Path -Parent $EntryAssembly
    $records = New-Object System.Collections.Generic.List[string]
    foreach ($file in @(Get-NightlyOutputFiles $artifactDirectory | Sort-Object FullName)) {
        $relativePath = Get-RepoRelativePath -BasePath $artifactDirectory -TargetPath $file.FullName
        $records.Add("$relativePath`t$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)")
    }

    # Ordinal ordering makes the fingerprint identical in Windows PowerShell 5.1
    # and PowerShell 7, whose culture-sensitive sort orders can differ.
    [string[]] $canonicalRecords = $records.ToArray()
    [Array]::Sort($canonicalRecords, [StringComparer]::Ordinal)
    return [pscustomobject] [ordered]@{
        path = Get-RepoRelativePath -BasePath $RepoRoot -TargetPath $artifactDirectory
        sha256 = Get-Sha256ForText -Text ($canonicalRecords -join "`n")
        fileCount = $records.Count
    }
}

# Read-only selection of already compiled outputs. Build receipts contain metadata only.
function Assert-NightlyOutputPath {
    param([string] $RepoRoot, [string] $Path)
    $resolved = Resolve-ConfiguredPath $RepoRoot $Path
    $cursor = $resolved
    while ($cursor -and $cursor -ne $RepoRoot) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Output links are not supported: $resolved"
        }
        $cursor = Split-Path -Parent $cursor
    }
    return $resolved
}

function Get-NightlyOutputFiles {
    param([string] $Directory)
    foreach ($item in @(Get-ChildItem -LiteralPath $Directory -Force | Sort-Object Name)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Output links are not supported: $($item.FullName)"
        }
        if ($item.PSIsContainer) { Get-NightlyOutputFiles $item.FullName }
        else { $item }
    }
}

function Open-NightlyOutputLocks {
    param($Configuration, [string] $RepoRoot)
    $streams = New-Object 'System.Collections.Generic.List[System.IO.FileStream]'
    try {
        foreach ($stage in $Configuration.stages) {
            $entry = Assert-NightlyOutputPath $RepoRoot ([string] $stage.entryAssembly)
            $directory = Split-Path -Parent $entry
            $stem = [IO.Path]::GetFileNameWithoutExtension($entry)
            foreach ($required in @($entry, (Join-Path $directory "$stem.deps.json"), (Join-Path $directory "$stem.runtimeconfig.json"))) {
                if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
                    throw "Required compiled output is missing: $required. No build or fallback will be attempted."
                }
            }
            foreach ($file in @(Get-NightlyOutputFiles $directory)) {
                # Keep existing files readable by the applications but deny overwrite/deletion
                # until all stages finish. This opens files in place; it never copies them.
                $streams.Add([IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
            }
        }
        return [pscustomobject]@{ Streams = $streams }
    }
    catch {
        foreach ($stream in $streams) { $stream.Dispose() }
        throw
    }
}

function Close-NightlyOutputLocks {
    param($OutputLocks)
    if ($null -ne $OutputLocks) {
        foreach ($stream in $OutputLocks.Streams) { $stream.Dispose() }
    }
}

function Read-NightlyBuildReceiptText {
    param([string] $Path)
    # An atomic replace may occur while nightly reads. The open handle keeps the old
    # complete receipt readable; the next run sees the replacement, never half a JSON file.
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = New-Object IO.StreamReader($stream)
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

function Get-NightlyCompiledIdentity {
    param([string] $EntryAssembly)
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($EntryAssembly).ProductVersion
    if ($version -notmatch '\+([a-fA-F0-9]{40})(?:$|[.+-])') {
        throw "Compiled source commit is unavailable in '$EntryAssembly'; nightly registration refused."
    }
    return [pscustomobject]@{ sourceCommit=$Matches[1]; informationalVersion=$version }
}

function Register-NightlyBuildOutput {
    param([string] $ApplicationName, [string] $EntryAssembly, [string] $RepoRoot, [string] $RegistryDirectory)
    $completedUtc = [DateTimeOffset]::UtcNow
    if ($ApplicationName -notin @('Hermes', 'DataAudit', 'Delphi', 'Athena') -or
        [IO.Path]::GetFileName($EntryAssembly) -ne "$ApplicationName.dll") {
        throw 'Only the four nightly application entry assemblies can be registered.'
    }
    $entry = Assert-NightlyOutputPath $RepoRoot $EntryAssembly
    $registry = Assert-NightlyOutputPath $RepoRoot $RegistryDirectory
    $outputDirectory = (Split-Path -Parent $entry).TrimEnd('\') + '\'
    if (($registry.TrimEnd('\') + '\').StartsWith($outputDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The metadata registry must be outside the compiled output directory.'
    }
    [void] [IO.Directory]::CreateDirectory($registry)
    $receiptPath = Assert-NightlyOutputPath $RepoRoot (Join-Path $registry "$ApplicationName.json")
    $lockPath = Assert-NightlyOutputPath $RepoRoot (Join-Path $registry "$ApplicationName.lock")
    $lock = $null
    $outputLocks = $null
    $temporaryPath = $null
    try {
        # Serialize only registrations of this application, not other builds or apps.
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
        while ($null -eq $lock) {
            try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
            catch [IO.IOException] {
                if ([DateTimeOffset]::UtcNow -ge $deadline) { throw }
                Start-Sleep -Milliseconds 100
            }
        }
        if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
            $previous = (Read-NightlyBuildReceiptText $receiptPath) | ConvertFrom-Json
            Assert-NightlyBuildReceipt $previous $ApplicationName $RepoRoot
            if ([DateTimeOffset]::Parse([string] $previous.registeredUtc) -gt $completedUtc) { return $previous }
        }
        $oneStage = [pscustomobject]@{ stages=@([pscustomobject]@{entryAssembly=$entry}) }
        $outputLocks = Open-NightlyOutputLocks $oneStage $RepoRoot
        $identity = Get-NightlyCompiledIdentity $entry
        $snapshot = Get-ArtifactSnapshot $RepoRoot $entry
        $receipt = [pscustomobject] [ordered]@{
            schemaVersion = 1
            directoryHashFormat = 'RelativePathTabSha256OrdinalV1'
            name = $ApplicationName
            configuration = 'Release'
            entryAssembly = Get-RepoRelativePath $RepoRoot $entry
            registeredUtc = $completedUtc.ToString('O')
            sourceCommit = $identity.sourceCommit
            # HEAD identifies the compiled base revision, not uncommitted input bytes.
            # Do not claim a Clean/Dirty state from a checkout that may have changed.
            workingTreeState = 'Unknown'
            informationalVersion = $identity.informationalVersion
            sha256 = $snapshot.sha256
            fileCount = $snapshot.fileCount
        }
        Assert-NightlyBuildReceipt $receipt $ApplicationName $RepoRoot
        $temporaryPath = Join-Path $registry "$ApplicationName.$([Guid]::NewGuid().ToString('N')).tmp"
        [IO.File]::WriteAllText($temporaryPath, ($receipt | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $receiptPath -PathType Leaf) { [IO.File]::Replace($temporaryPath, $receiptPath, [NullString]::Value) }
        else { [IO.File]::Move($temporaryPath, $receiptPath) }
        return $receipt
    }
    finally {
        if ($temporaryPath -and (Test-Path -LiteralPath $temporaryPath)) { [IO.File]::Delete($temporaryPath) }
        Close-NightlyOutputLocks $outputLocks
        if ($null -ne $lock) { $lock.Dispose() }
    }
}

function Resolve-NightlyOutputSelection {
    param($Configuration, [string] $RepoRoot)
    $manifestPath = Assert-NightlyOutputPath $RepoRoot ([string] $Configuration.outputsManifest)
    $manifestText = [IO.File]::ReadAllText($manifestPath)
    $manifest = $manifestText | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.directoryHashFormat -ne 'RelativePathTabSha256OrdinalV1' -or
        [string]::IsNullOrWhiteSpace([string] $manifest.outputSetId)) {
        throw 'Unsupported or unidentified existing-output manifest.'
    }
    if ($manifest.sourceCommit -notmatch '^[a-fA-F0-9]{40}$' -or
        $manifest.workingTreeState -notin @('Clean', 'Dirty', 'Unknown')) {
        throw 'Existing-output build provenance is missing or invalid.'
    }
    if (@($manifest.stages).Count -ne @($Configuration.stages).Count) {
        throw 'Output manifest must identify every configured stage exactly once.'
    }
    $selectedStages = @()
    $hasRegistration = $false
    $registry = if ($Configuration.PSObject.Properties.Name -contains 'outputRegistry') {
        Assert-NightlyOutputPath $RepoRoot ([string] $Configuration.outputRegistry)
    } else { $null }
    foreach ($stage in $Configuration.stages) {
        $selected = @($manifest.stages | Where-Object name -eq $stage.name)
        if ($selected.Count -ne 1) { throw "Output manifest must identify '$($stage.name)' exactly once." }
        $expected = $selected[0]
        $entry = Assert-NightlyOutputPath $RepoRoot ([string] $stage.entryAssembly)
        $selectedEntry = Assert-NightlyOutputPath $RepoRoot ([string] $expected.entryAssembly)
        if ($entry -ne $selectedEntry -or $expected.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $expected.fileCount -lt 3) {
            throw "Output manifest does not match the selected files for '$($stage.name)'."
        }
        $candidate = [pscustomobject]@{
            name = [string] $stage.name
            entryAssembly = [string] $expected.entryAssembly
            sha256 = [string] $expected.sha256
            fileCount = [int] $expected.fileCount
            sourceCommit = [string] $manifest.sourceCommit
            workingTreeState = [string] $manifest.workingTreeState
            registeredUtc = $null
            receiptSha256 = $null
        }
        if ($registry) {
            $receiptPath = Assert-NightlyOutputPath $RepoRoot (Join-Path $registry "$($stage.name).json")
            if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
                # One atomic read freezes this app's choice for the entire run.
                $receiptText = Read-NightlyBuildReceiptText $receiptPath
                $receipt = $receiptText | ConvertFrom-Json
                Assert-NightlyBuildReceipt $receipt ([string] $stage.name) $RepoRoot
                $candidate = $receipt
                $candidate | Add-Member -NotePropertyName receiptSha256 -NotePropertyValue (Get-Sha256ForText $receiptText)
                $hasRegistration = $true
            }
        }
        $selectedStages += $candidate
    }
    $commits = @($selectedStages.sourceCommit | Select-Object -Unique)
    $states = @($selectedStages.workingTreeState | Select-Object -Unique)
    return [pscustomobject]@{
        outputSetId = if ($hasRegistration) { 'registered-' + (Get-Sha256ForText ($selectedStages | ConvertTo-Json -Depth 5 -Compress)) } else { [string] $manifest.outputSetId }
        sourceCommit = if ($commits.Count -eq 1) { $commits[0] } else { 'Mixed' }
        workingTreeState = if ($states.Count -eq 1) { $states[0] } else { 'Mixed' }
        manifestSha256 = Get-Sha256ForText $manifestText
        stages = $selectedStages
    }
}

function Assert-NightlyBuildReceipt {
    param($Receipt, [string] $ApplicationName, [string] $RepoRoot)
    $registered = [DateTimeOffset]::MinValue
    if ($Receipt.schemaVersion -ne 1 -or $Receipt.directoryHashFormat -ne 'RelativePathTabSha256OrdinalV1' -or
        $Receipt.name -ne $ApplicationName -or $Receipt.configuration -ne 'Release' -or
        [IO.Path]::GetFileName([string] $Receipt.entryAssembly) -ne "$ApplicationName.dll" -or
        $Receipt.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $Receipt.fileCount -lt 3 -or
        $Receipt.sourceCommit -notmatch '^[a-fA-F0-9]{40}$' -or
        $Receipt.workingTreeState -notin @('Clean', 'Dirty', 'Unknown') -or
        -not [DateTimeOffset]::TryParse([string] $Receipt.registeredUtc, [ref] $registered)) {
        throw "Invalid successful Release build receipt for '$ApplicationName'. No fallback will be attempted."
    }
    [void] (Assert-NightlyOutputPath $RepoRoot ([string] $Receipt.entryAssembly))
}

function Get-NightlyExistingOutputs {
    param($Configuration, [string] $RepoRoot, $Selection = $null)
    if ($null -eq $Selection) { $Selection = Resolve-NightlyOutputSelection $Configuration $RepoRoot }
    $artifacts = @{}
    foreach ($expected in $Selection.stages) {
        $entry = Assert-NightlyOutputPath $RepoRoot ([string] $expected.entryAssembly)
        $snapshot = Get-ArtifactSnapshot $RepoRoot $entry
        if ($snapshot.sha256 -ne $expected.sha256 -or $snapshot.fileCount -ne $expected.fileCount) {
            throw "Selected outputs for '$($expected.name)' changed or are incomplete. No stage started; no build or fallback will be attempted."
        }
        foreach ($property in @('entryAssembly', 'sourceCommit', 'workingTreeState', 'registeredUtc', 'receiptSha256')) {
            $snapshot | Add-Member -NotePropertyName $property -NotePropertyValue $expected.$property
        }
        $artifacts[[string] $expected.name] = $snapshot
    }
    return [pscustomobject]@{
        outputSetId = $Selection.outputSetId
        sourceCommit = $Selection.sourceCommit
        workingTreeState = $Selection.workingTreeState
        manifestSha256 = $Selection.manifestSha256
        artifacts = $artifacts
    }
}
