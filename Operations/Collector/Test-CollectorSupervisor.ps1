# Offline package-boundary tests. No collector, task, SQL or provider is started.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$root = Join-Path $repo 'artifacts\build\collector-supervisor-tests'
if (Test-Path -LiteralPath $root) {
    if ([IO.Path]::GetFullPath($root) -ne (Join-Path $repo 'artifacts\build\collector-supervisor-tests')) { throw 'Unexpected test path.' }
    Remove-Item -LiteralPath $root -Recurse -Force
}
$runtime = Join-Path $root 'runtime'
[IO.Directory]::CreateDirectory($runtime) | Out-Null
$control = Join-Path $root 'control'
$configPath = Join-Path $root 'collector.json'
@{ControlDirectory=$control} | ConvertTo-Json | Set-Content -LiteralPath $configPath
foreach ($name in @('collector.exe','core.dll','collector.deps.json')) { Set-Content -LiteralPath (Join-Path $runtime $name) -Value 'synthetic, not executable' }
$manifest = @{
    ExecutablePath=(Join-Path $runtime 'collector.exe'); ConfigurationPath=$configPath; ControlDirectory=$control
    ConfigurationSha256=(Get-FileHash -LiteralPath $configPath).Hash; MaximumRestarts=3
    Files=@(Get-ChildItem -LiteralPath $runtime -File | ForEach-Object { @{RelativePath=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
}
$manifestPath = Join-Path $root 'manifest.json'
function Save-Manifest { $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath }
function Check-Package { & (Join-Path $PSScriptRoot 'Watch-Collector.ps1') -ManifestPath $manifestPath -ValidateOnly }
function Expect-Rejection([string]$Name) {
    $rejected=$false
    try { Check-Package } catch { $rejected=$true }
    if (!$rejected) { throw "Unexpected acceptance: $Name" }
}
Save-Manifest
Check-Package
if (Test-Path -LiteralPath $control) { throw 'Validation created live control state.' }
Set-Content -LiteralPath (Join-Path $runtime 'unexpected.dll') -Value 'extra'
Expect-Rejection 'unexpected runtime file'
Remove-Item -LiteralPath (Join-Path $runtime 'unexpected.dll')
$original = $manifest.Files[0].Sha256
$manifest.Files[0].Sha256='bad'; Save-Manifest; Expect-Rejection 'changed assembly'
$manifest.Files[0].Sha256=$original
$relative = $manifest.Files[0].RelativePath
$manifest.Files[0].RelativePath='..\collector.json'; Save-Manifest; Expect-Rejection 'path traversal'
$manifest.Files[0].RelativePath=$relative
$manifest.ConfigurationSha256='bad'; Save-Manifest; Expect-Rejection 'changed configuration'
$manifest.ConfigurationSha256=(Get-FileHash -LiteralPath $configPath).Hash
$manifest.MaximumRestarts=6; Save-Manifest; Expect-Rejection 'unbounded restarts'
$manifest.MaximumRestarts=3
$executable=$manifest.ExecutablePath
$manifest.ExecutablePath='C:relative.exe'; Save-Manifest; Expect-Rejection 'drive-relative executable'
$manifest.ExecutablePath=$executable
$manifest.ControlDirectory=(Join-Path $root 'different-control'); Save-Manifest; Expect-Rejection 'different control directory'
$manifest.ControlDirectory=$control
Save-Manifest; Check-Package
'Passed: valid package, no validation side effects, and seven invalid-package cases.'
