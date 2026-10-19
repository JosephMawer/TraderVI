# One-time, explicitly authorized security installation. Never persist an approval for this script.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory, [switch]$ResumeAfterBackup, [switch]$ResumeVerification, [switch]$UpgradeHelperOnly)
$ErrorActionPreference = 'Stop'
$repo = 'C:\src\TraderVI'
$codexRoot = Join-Path $env:USERPROFILE '.codex'
$install = Join-Path $codexRoot 'tools\tradervi-read-only-db\v2'
$mailbox = Join-Path $repo 'artifacts\read-only-db'
$rulesPath = Join-Path $codexRoot 'rules\tradervi-read-only-db.rules'
$defaultRules = Join-Path $codexRoot 'rules\default.rules'
$pwsh = (Get-Process -Id $PID).Path
if ((Resolve-Path -LiteralPath $PSScriptRoot).Path -ne (Join-Path $repo 'Operations\ReadOnlyChecks')) { throw 'Unexpected repository location.' }
if ((Test-Path -LiteralPath $install) -and -not $ResumeVerification) { throw 'Installation already exists; review an upgrade instead of overwriting.' }
if ((Test-Path -LiteralPath $rulesPath) -and -not $UpgradeHelperOnly) { throw 'Approval rule already exists; do not overwrite.' }
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'TraderVI.ReadOnlyChecks.exe'))) { throw 'Publish the reviewed helper first.' }
$offline = & (Join-Path $PublishDirectory 'TraderVI.ReadOnlyChecks.exe') --self-test | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $offline.OfflinePolicyChecksPassed -ne 33) { throw 'Offline SQL policy verification failed.' }
New-Item -ItemType Directory -Path $mailbox -Force | Out-Null
Set-Location -LiteralPath $repo

if (-not $ResumeVerification) {
if (-not $UpgradeHelperOnly) {
# Required pre-migration backup, checksum verification and copy to the established backup destination.
$backupLog = Join-Path $mailbox 'installation-backup.log'
if (-not $ResumeAfterBackup) {
    & sqlcmd -S localhost -d master -E -b -r 1 -i TraderDB/Operations/Backup-TraderDB.sql -o $backupLog
    if ($LASTEXITCODE -ne 0) { throw 'Backup/verification failed; no diagnostic user was created.' }
} elseif (-not (Test-Path -LiteralPath $backupLog)) { throw 'No prepared backup evidence exists.' }
$backupJson = & sqlcmd -S localhost -d master -E -b -y 0 -w 65535 -Q "SET NOCOUNT ON; SELECT TOP (1) m.physical_device_name AS BackupFile,b.has_backup_checksums AS Checksums,b.is_damaged AS Damaged FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily m ON m.media_set_id=b.media_set_id WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.backup_finish_date>=DATEADD(minute,-30,GETDATE()) ORDER BY b.backup_finish_date DESC FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the fresh backup.' }
$backup = (($backupJson | Where-Object { $_.Trim().StartsWith('{') }) -join '') | ConvertFrom-Json
if (-not $backup.Checksums -or $backup.Damaged) { throw 'Backup checksum/damage verification failed.' }
if ($ResumeAfterBackup) {
    $escapedBackup = $backup.BackupFile.Replace("'", "''")
    & sqlcmd -S localhost -d master -E -b -Q "RESTORE VERIFYONLY FROM DISK=N'$escapedBackup' WITH CHECKSUM;" -o (Join-Path $mailbox 'installation-backup-reverify.log')
    if ($LASTEXITCODE -ne 0) { throw 'Prepared backup re-verification failed.' }
}
$destination = $env:TRADERVI_BACKUP_DESTINATION_DIRECTORY
if (-not $destination) {
    if (-not $env:OneDrive) { throw 'Established off-machine backup destination is unavailable.' }
    $destination = Join-Path $env:OneDrive 'Joseph\Tradervi\backups'
}
if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Backup destination does not exist.' }
$copyPath = Join-Path $destination ([IO.Path]::GetFileName($backup.BackupFile))
if (Test-Path -LiteralPath $copyPath) { throw 'Backup copy already exists; no overwrite allowed.' }
[IO.File]::Copy($backup.BackupFile, $copyPath, $false)
$backupHash = (Get-FileHash -LiteralPath $backup.BackupFile -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $copyPath -Algorithm SHA256).Hash -ne $backupHash) { throw 'Backup copy checksum mismatch.' }
[pscustomobject]@{VerifiedUtc=[DateTimeOffset]::UtcNow;StagingFile=$backup.BackupFile;CopyFile=$copyPath;Sha256=$backupHash} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mailbox 'installation-backup.json')
& sqlcmd -S localhost -d TraderDB -E -b -i TraderDB/Migrations/20261018_046_AddReadOnlyDiagnosticUser.sql -o (Join-Path $mailbox 'installation-migration.log')
if ($LASTEXITCODE -ne 0) { throw 'Security migration failed; no helper approval was installed.' }
} else {
    $previous = Get-Content -LiteralPath (Join-Path $mailbox 'installation.json') -Raw | ConvertFrom-Json
    if ($previous.Migration -ne '20261018_046_AddReadOnlyDiagnosticUser.sql') { throw 'Unexpected existing security installation.' }
    $backupHash = $previous.BackupSha256
    Copy-Item -LiteralPath (Join-Path $mailbox 'installation.json') -Destination (Join-Path $mailbox 'installation-v1.json')
    Copy-Item -LiteralPath (Join-Path $mailbox 'verification.json') -Destination (Join-Path $mailbox 'verification-v1.json')
}

New-Item -ItemType Directory -Path $install | Out-Null
Get-ChildItem -LiteralPath $PublishDirectory -Force | Copy-Item -Destination $install -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke.ps1') -Destination (Join-Path $install 'Invoke.ps1')
$manifest = @(Get-ChildItem -LiteralPath $install -File -Recurse | ForEach-Object {
    [pscustomobject]@{Path=[IO.Path]::GetRelativePath($install,$_.FullName);Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $install 'files.json')
} else {
    $manifest = @(Get-Content -LiteralPath (Join-Path $install 'files.json') -Raw | ConvertFrom-Json)
    $backupHash = (Get-Content -LiteralPath (Join-Path $mailbox 'installation-backup.json') -Raw | ConvertFrom-Json).Sha256
}
& (Join-Path $PSScriptRoot 'Verify.ps1') -InstalledDirectory $install
$nativeCount = @(Get-Content -LiteralPath (Join-Path $mailbox 'verification.json') -Raw | ConvertFrom-Json).Count
if ($nativeCount -ne 12) { throw 'Unexpected native verification count.' }

# Enable only after native denial checks pass. The wrapper command is constant across requests.
$invoke = "& '$pwsh' -NoProfile -File '$install\Invoke.ps1'"
$patterns = @(
    ,@($pwsh, '-NoProfile', '-File', "$install\Invoke.ps1")
)
$rules = "# TraderVI fixed local read-only helper; reviewed installation, no CLI arguments.`n"
foreach ($pattern in $patterns) {
    $encoded = ConvertTo-Json -InputObject $pattern -Compress
    $rules += "prefix_rule(pattern=$encoded, decision=`"allow`")`n"
}
$candidate = Join-Path $mailbox 'approval-candidate.rules'
[IO.File]::WriteAllText($candidate, $rules, [Text.UTF8Encoding]::new($false))
$policy = & codex execpolicy check --rules $candidate $pwsh -NoProfile -File "$install\Invoke.ps1" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $policy.decision -ne 'allow') { throw 'Fixed-command approval did not match.' }
$policy = & codex execpolicy check --rules $candidate $pwsh -NoProfile -File (Join-Path $repo 'Operations\ReadOnlyChecks\Invoke.ps1') | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $policy.decision -eq 'allow') { throw 'Editable repository wrapper must not be approved.' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
if ($UpgradeHelperOnly) {
    $oldHelperRules = [IO.File]::ReadAllText($rulesPath)
    $oldLines = @($oldHelperRules -split '\r?\n' | Where-Object { $_ -and -not $_.StartsWith('#') })
    if ($oldLines.Count -lt 1 -or $oldLines.Count -gt 2 -or @($oldLines | Where-Object { -not $_.Contains('tradervi-read-only-db\\v1\\Invoke.ps1') }).Count) {
        throw 'Existing helper rules changed; review before replacing.'
    }
    [IO.File]::WriteAllText("$rulesPath.backup-$stamp", $oldHelperRules, [Text.UTF8Encoding]::new($false))
}
$original = [IO.File]::ReadAllText($defaultRules)
$broad = 'prefix_rule(pattern=["sqlcmd", "-S", "localhost", "-d", "TraderDB", "-E"], decision="allow")'
$lines = @($original -split '\r?\n' | Where-Object { $_ -ne $broad })
if ($lines.Count -ne @($original -split '\r?\n').Count) {
    [IO.File]::WriteAllText("$defaultRules.backup-$stamp", $original, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($defaultRules, ($lines -join "`n"), [Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText($rulesPath, $rules, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{InstalledUtc=[DateTimeOffset]::UtcNow;InstalledDirectory=$install;InvokeCommand=$invoke;
    RulesFile=$rulesPath;DatabaseUser='TraderVIReadOnlyChecks';BackupSha256=$backupHash;Files=$manifest.Count;
    Migration='20261018_046_AddReadOnlyDiagnosticUser.sql';NativeChecksPassed=12;OfflinePolicyChecksPassed=33;RestartOtherChats=$true} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mailbox 'installation.json')
'Read-only helper installed; 12 native checks passed; narrow approvals saved. Restart existing Codex chats to reload rules.'
