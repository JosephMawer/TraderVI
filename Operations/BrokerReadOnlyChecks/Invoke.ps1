# Installed copy only. Never approve the editable repository copy.
$ErrorActionPreference = 'Stop'
if ($args.Count -ne 0) { throw 'The read-only broker helper accepts no arguments.' }
Set-Location -LiteralPath $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'files.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    if ($entry.Path -match '(^[\\/]|\.\.|:)') { throw 'Invalid installed manifest path.' }
    $file = Join-Path $PSScriptRoot $entry.Path
    if ((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installed link rejected.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Sha256) { throw 'Installed helper hash mismatch; re-review installation.' }
}
Get-ChildItem Env: | Where-Object Name -Match '^(DOTNET_|CORECLR_|COR_|COMPLUS_)' |
    ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_.Name) }
& (Join-Path $PSScriptRoot 'TraderVI.BrokerReadOnlyChecks.exe')
exit $LASTEXITCODE
