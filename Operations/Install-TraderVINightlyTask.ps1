[CmdletBinding()]
param(
    [string] $ConfigurationPath,

    [string] $StateDirectory,

    [switch] $SkipTaskRegistration
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $ConfigurationPath = Join-Path $PSScriptRoot 'nightly-runner.json'
}
if ([string]::IsNullOrWhiteSpace($StateDirectory)) {
    $StateDirectory = Join-Path $env:LOCALAPPDATA 'TraderVI\Nightly'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedConfigurationPath = (Resolve-Path -LiteralPath $ConfigurationPath).Path
$configuration = Get-Content -LiteralPath $resolvedConfigurationPath -Raw | ConvertFrom-Json
if ($configuration.schemaVersion -ne 2) {
    throw "Unsupported nightly-runner configuration schema '$($configuration.schemaVersion)'."
}

if ([TimeZoneInfo]::Local.Id -notin @('Eastern Standard Time', 'America/Toronto')) {
    throw "The Windows task uses the machine's local clock, but this computer is set to '$([TimeZoneInfo]::Local.Id)' instead of Toronto/Eastern time."
}

$pwshPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $pwshPath -PathType Leaf)) {
    throw "Stable Windows PowerShell executable not found: $pwshPath"
}

$runnerPath = Join-Path $PSScriptRoot 'Invoke-TraderVINightly.ps1'
$resolvedStateDirectory = [System.IO.Path]::GetFullPath($StateDirectory)
[System.IO.Directory]::CreateDirectory($resolvedStateDirectory) | Out-Null

& $pwshPath `
    -NoLogo `
    -NoProfile `
    -NonInteractive `
    -ExecutionPolicy Bypass `
    -File $runnerPath `
    -ConfigurationPath $resolvedConfigurationPath `
    -StateDirectory $resolvedStateDirectory `
    -ValidateOnly
if ($LASTEXITCODE -ne 0) {
    throw "Nightly latest-source configuration validation failed with exit code $LASTEXITCODE."
}

if ($SkipTaskRegistration) {
    Write-Host 'Validated the latest-source runner and skipped Windows Task Scheduler registration.'
    return
}

$taskName = [string] $configuration.task.name
$actionArguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runnerPath`" -ConfigurationPath `"$resolvedConfigurationPath`" -StateDirectory `"$resolvedStateDirectory`""
$action = New-ScheduledTaskAction -Execute $pwshPath -Argument $actionArguments -WorkingDirectory $repoRoot
$days = @($configuration.task.daysOfWeek)
$trigger = New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek $days -At ([string] $configuration.task.localTime)
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -WakeToRun `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Hours ([int] $configuration.task.executionTimeLimitHours))
$principal = New-ScheduledTaskPrincipal `
    -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive `
    -RunLevel Limited

$task = New-ScheduledTask `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description 'Builds the current TraderVI source, then runs Hermes, Delphi, and Athena in a guarded nightly pipeline. Does not place broker orders.'

Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
Write-Host "Registered Windows task '$taskName' for $($configuration.task.localTime) on $($days -join ', ')."
Write-Host 'Each run builds the current source without restoring dependencies, verifies source stability, and then runs those exact artifacts.'
Write-Host 'The task runs only while this Windows user is signed in; WakeToRun and StartWhenAvailable are enabled.'
