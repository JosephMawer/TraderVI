[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CollectorConfigurationPath,
    [string]$CalendarPath,
    [string]$StateDirectory = (Join-Path $env:LOCALAPPDATA 'TraderVI\Launchers'),
    [switch]$ValidateOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$runner = Join-Path $PSScriptRoot 'Invoke-TraderVIApplication.ps1'
if (!$CalendarPath) { $CalendarPath = Join-Path $repo 'Operations\Calendars\tsx-2026-through-20261223-v1.json' }
$CalendarPath = (Resolve-Path -LiteralPath $CalendarPath).Path
$CollectorConfigurationPath = (Resolve-Path -LiteralPath $CollectorConfigurationPath).Path
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
$configurationPath = Join-Path $StateDirectory 'applications.json'
$manifestPath = Join-Path $StateDirectory 'collector-supervisor.json'
foreach ($path in @($shell, $runner, $configurationPath, $CollectorConfigurationPath)) {
    if ($path.Contains('"')) { throw 'Unsupported quote in application path.' }
}
$collectorRoot = Join-Path $repo 'TraderVI.Collector\bin\Release\net10.0'
$collectorExecutable = Join-Path $collectorRoot 'TraderVI.Collector.exe'
$desktopRoot = Join-Path $repo 'TraderVI.WPF\bin\Release\net10.0-windows'
foreach ($path in @($shell, $collectorExecutable, (Join-Path $desktopRoot 'TraderVI.Engine.exe'), (Join-Path $desktopRoot 'TraderVI.WPF.exe'))) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required existing output missing: $path" }
}
$collector = Get-Content -LiteralPath $CollectorConfigurationPath -Raw | ConvertFrom-Json
if ($collector.CalendarPath -ne $CalendarPath) { throw 'Applications must use the collector reviewed calendar.' }
$files = @(Get-ChildItem -LiteralPath $collectorRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    @{ RelativePath=$_.FullName.Substring($collectorRoot.Length + 1); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$manifest = [ordered]@{
    ExecutablePath=$collectorExecutable; ConfigurationPath=$CollectorConfigurationPath
    ConfigurationSha256=(Get-FileHash -LiteralPath $CollectorConfigurationPath -Algorithm SHA256).Hash
    ControlDirectory=$collector.ControlDirectory; MaximumRestarts=3; Files=$files
}
$configuration = [ordered]@{ SchemaVersion=1; Repository=$repo; CalendarPath=$CalendarPath; CollectorManifest=$manifestPath }
if ($ValidateOnly) {
    [pscustomobject]@{ Repository=$repo; CollectorExecutable=$collectorExecutable; CollectorFiles=$files.Count; TaskNames='TraderVI Engine, TraderVI Collector, TraderVI Dashboard'; Triggers='None' }
    return
}
# Refuse partial replacement of an existing or running installation. Rebuilding the collector
# requires an explicit stopped-host re-registration to refresh its complete file manifest.
$definitions = @()
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
foreach ($application in @('Engine', 'Collector', 'Dashboard')) {
    $name = "TraderVI $application"
    $arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$runner`" -Application $application -ConfigurationPath `"$configurationPath`""
    $existing = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    if ($null -ne $existing) {
        if ($existing.State -eq 'Running') { throw "Stop $name normally before updating its registration." }
        $existingUser = $existing.Principal.UserId
        if ($existingUser -notmatch '^S-1-') { $existingUser = ([Security.Principal.NTAccount]$existingUser).Translate([Security.Principal.SecurityIdentifier]).Value }
        if (@($existing.Actions).Count -ne 1 -or $existing.Actions[0].Execute -ne $shell -or $existing.Actions[0].Arguments -ne $arguments -or
            @($existing.Triggers | Where-Object { $null -ne $_ }).Count -ne 0 -or $existingUser -ne $userSid) { throw "Existing $name differs; review it before replacement." }
    }
    $action = New-ScheduledTaskAction -Execute $shell -Argument $arguments -WorkingDirectory $repo
    $task = New-ScheduledTask -Action $action -Principal $principal -Settings $settings -Description "On-demand $application from the main checkout's Release output. No build, copying, sign-in trigger or financial permission changes."
    $definitions += @{Name=$name; Task=$task}
}
[IO.Directory]::CreateDirectory($StateDirectory) | Out-Null
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$configuration | ConvertTo-Json | Set-Content -LiteralPath $configurationPath -Encoding UTF8
foreach ($application in @('Engine', 'Collector', 'Dashboard')) {
    & $runner -Application $application -ConfigurationPath $configurationPath -ValidateOnly
}
foreach ($definition in $definitions) { Register-ScheduledTask -TaskName $definition.Name -InputObject $definition.Task -Force | Out-Null }
'Registered three on-demand Windows tasks. No application was started.'
