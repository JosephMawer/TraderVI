[CmdletBinding()]
param(
    [string] $ConfigurationPath,

    [string] $StateDirectory,

    [switch] $SkipTaskRegistration
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NightlyNextLocalStart {
    param([string] $LocalTime, [DateTime] $Now = [DateTime]::Now)
    $time = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($LocalTime, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref] $time)) {
        throw 'Nightly local time must be HH:mm.'
    }
    $next = [DateTime]::SpecifyKind($Now.Date.Add($time.TimeOfDay), [DateTimeKind]::Unspecified)
    if ($next -le $Now) { $next = $next.AddDays(1) }
    return $next
}

if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $ConfigurationPath = Join-Path $PSScriptRoot 'nightly-runner.json'
}
if ([string]::IsNullOrWhiteSpace($StateDirectory)) {
    $StateDirectory = Join-Path $env:LOCALAPPDATA 'TraderVI\Nightly'
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedConfigurationPath = (Resolve-Path -LiteralPath $ConfigurationPath).Path
$configuration = Get-Content -LiteralPath $resolvedConfigurationPath -Raw | ConvertFrom-Json
if ($configuration.schemaVersion -ne 4) {
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
    throw "Nightly existing-output validation failed with exit code $LASTEXITCODE."
}

if ($SkipTaskRegistration) {
    Write-Host 'Validated existing outputs and skipped Windows Task Scheduler registration.'
    return
}

$taskName = [string] $configuration.task.name
$actionArguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runnerPath`" -ConfigurationPath `"$resolvedConfigurationPath`" -StateDirectory `"$resolvedStateDirectory`""
$action = New-ScheduledTaskAction -Execute $pwshPath -Argument $actionArguments -WorkingDirectory $repoRoot
$days = @($configuration.task.daysOfWeek)
# Use a floating local time so daylight-saving transitions preserve 06:30 Toronto.
# Start in the future: updating the task must not trigger a missed occurrence now.
$nextStart = Get-NightlyNextLocalStart ([string] $configuration.task.localTime)
$trigger = New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek $days -At $nextStart
$trigger.StartBoundary = $nextStart.ToString("yyyy-MM-dd'T'HH:mm:ss", [Globalization.CultureInfo]::InvariantCulture)
$description = 'Runs the selected existing Hermes, DataAudit, Delphi and Athena outputs in place. No restore, compilation or binary/source copying. Does not place broker orders.'
$existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask) {
    if ($existingTask.State -eq 'Running') { throw 'The nightly task is running; its registration was not changed.' }
    if (@($existingTask.Actions).Count -ne 1 -or
        $existingTask.Actions[0].Execute -ne $pwshPath -or
        $existingTask.Actions[0].Arguments -ne $actionArguments -or
        $existingTask.Actions[0].WorkingDirectory -ne $repoRoot) {
        throw 'The existing task action differs from the reviewed runner; inspect it before changing registration.'
    }
    # Preserve the installed principal, action and settings. Only schedule/description change.
    $existingTask.Triggers = @($trigger)
    $existingTask.Description = $description
    Set-ScheduledTask -InputObject $existingTask | Out-Null
    Write-Host "Updated '$taskName' to $($configuration.task.localTime) local time; all other task settings retained. No run started."
    return
}
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
    -Description $description

Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
Write-Host "Registered Windows task '$taskName' for $($configuration.task.localTime) on $($days -join ', ')."
Write-Host 'Each run verifies and reuses the selected existing outputs. Nothing is restored, compiled or copied.'
Write-Host 'The task runs only while this Windows user is signed in; WakeToRun and StartWhenAvailable are enabled.'
