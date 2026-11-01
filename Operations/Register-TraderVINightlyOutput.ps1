[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Hermes', 'DataAudit', 'Delphi', 'Athena')]
    [string] $ApplicationName,
    [Parameter(Mandatory = $true)] [string] $EntryAssembly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NightlyOutputs.ps1')
$repoRoot = Split-Path -Parent $PSScriptRoot
$configuration = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'nightly-runner.json') -Raw | ConvertFrom-Json
$receipt = Register-NightlyBuildOutput $ApplicationName $EntryAssembly $repoRoot ([string] $configuration.outputRegistry)
Write-Host "Nightly Release registered: $($receipt.name) -> $($receipt.entryAssembly). Metadata only; no binaries copied or applications started."
