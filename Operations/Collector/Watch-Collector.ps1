param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Explicitly invoked supervisor. Installation/registering startup is a separate reviewed operation.
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
foreach ($path in @($manifest.ExecutablePath, $manifest.ConfigurationPath, $manifest.ControlDirectory)) {
    if (![IO.Path]::IsPathFullyQualified($path)) { throw 'Absolute collector paths required' }
}
function Assert-Package {
    $root = [IO.Path]::GetFullPath([IO.Path]::GetDirectoryName($manifest.ExecutablePath)).TrimEnd('\') + '\'
    if (@($manifest.Files).Count -lt 3) { throw 'Complete collector file manifest required' }
    $expected = @()
    foreach ($file in $manifest.Files) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $root $file.RelativePath))
        if (!$resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or $expected -contains $resolved -or
            (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash -ne $file.Sha256) { throw 'Collector file manifest mismatch' }
        $expected += $resolved
    }
    if ($expected -notcontains [IO.Path]::GetFullPath($manifest.ExecutablePath)) { throw 'Executable missing from file manifest' }
    $actual = @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object FullName)
    if ($actual.Count -ne $expected.Count -or @($actual | Where-Object { $expected -notcontains $_ }).Count -gt 0) { throw 'Unexpected collector package file' }
    if ((Get-FileHash -LiteralPath $manifest.ConfigurationPath -Algorithm SHA256).Hash -ne $manifest.ConfigurationSha256) { throw 'Collector configuration changed' }
}
Assert-Package
if ($manifest.MaximumRestarts -lt 0 -or $manifest.MaximumRestarts -gt 5) { throw 'A bounded restart allowance is required' }
$configuration = Get-Content -LiteralPath $manifest.ConfigurationPath -Raw | ConvertFrom-Json
if ([IO.Path]::GetFullPath($configuration.ControlDirectory) -ne [IO.Path]::GetFullPath($manifest.ControlDirectory)) { throw 'Control directory mismatch' }
[IO.Directory]::CreateDirectory($manifest.ControlDirectory) | Out-Null
$lock = [IO.File]::Open((Join-Path $manifest.ControlDirectory 'supervisor.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $stop = Join-Path $manifest.ControlDirectory 'stop.request'
    for ($attempt = 0; $attempt -le $manifest.MaximumRestarts; $attempt++) {
        if (Test-Path -LiteralPath $stop) { break }
        # Recheck before every restart. Never relaunch replacement binaries using a stale manifest.
        Assert-Package
        if ($manifest.ConfigurationPath.Contains('"')) { throw 'Invalid configuration path' }
        $process = Start-Process -FilePath $manifest.ExecutablePath -ArgumentList @('--run', ('"' + $manifest.ConfigurationPath + '"')) -PassThru -WindowStyle Hidden
        while (!$process.HasExited) { Start-Sleep -Seconds 1; $process.Refresh() }
        if ($process.ExitCode -eq 0 -or (Test-Path -LiteralPath $stop)) { break }
        if ($attempt -eq $manifest.MaximumRestarts) { throw 'Collector restart budget exhausted; operator attention required' }
        Start-Sleep -Seconds 10
    }
} finally { $lock.Dispose() }
