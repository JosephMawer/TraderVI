# Read-only preparation of explicitly selected, already compiled application outputs.
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

function Get-NightlyExistingOutputs {
    param($Configuration, [string] $RepoRoot)
    $manifestPath = Assert-NightlyOutputPath $RepoRoot ([string] $Configuration.outputsManifest)
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
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
    $artifacts = @{}
    foreach ($stage in $Configuration.stages) {
        $selected = @($manifest.stages | Where-Object name -eq $stage.name)
        if ($selected.Count -ne 1) { throw "Output manifest must identify '$($stage.name)' exactly once." }
        $expected = $selected[0]
        $entry = Assert-NightlyOutputPath $RepoRoot ([string] $stage.entryAssembly)
        $selectedEntry = Assert-NightlyOutputPath $RepoRoot ([string] $expected.entryAssembly)
        if ($entry -ne $selectedEntry -or $expected.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $expected.fileCount -lt 3) {
            throw "Output manifest does not match the selected files for '$($stage.name)'."
        }
        $snapshot = Get-ArtifactSnapshot $RepoRoot $entry
        if ($snapshot.sha256 -ne $expected.sha256 -or $snapshot.fileCount -ne $expected.fileCount) {
            throw "Selected outputs for '$($stage.name)' changed or are incomplete. No stage started; no build or fallback will be attempted."
        }
        $artifacts[[string] $stage.name] = $snapshot
    }
    return [pscustomobject]@{
        outputSetId = [string] $manifest.outputSetId
        sourceCommit = [string] $manifest.sourceCommit
        workingTreeState = [string] $manifest.workingTreeState
        manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        artifacts = $artifacts
    }
}
