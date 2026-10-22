# Explicitly authorized ADR-0103 maintenance only. Never launch an application or enable trading here.
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Backup','047','048','049','050','051')][string]$Step)
$ErrorActionPreference = 'Stop'
$repo = 'C:\src\TraderVI'
$evidence = Join-Path $repo 'artifacts\independent-engine\native'
if ((Resolve-Path -LiteralPath $PSScriptRoot).Path -ne (Join-Path $repo 'Operations')) { throw 'Unexpected checkout.' }
if (!(Test-Path -LiteralPath $evidence -PathType Container)) { throw 'Prepare the maintenance evidence directory first.' }
if (@(Get-Process -Name TraderVI,TraderVI.WPF,TraderVI.Engine,Delphi,Hermes,Hercules,Oracle,Sandbox -ErrorAction SilentlyContinue).Count) {
    throw 'An operational process is running. Stop it normally before maintenance.'
}
$nightly = Get-ScheduledTask -TaskName 'TraderVI Nightly' -TaskPath '\'
if ($nightly.Settings.Enabled -or $nightly.State -eq 'Running') { throw 'Nightly must remain stopped and disabled during maintenance.' }
$backupRecord = Join-Path $evidence 'verified-backup.json'
if ($Step -eq 'Backup') {
    if (Test-Path -LiteralPath $backupRecord) { throw 'A backup receipt already exists; inspect it instead of overwriting.' }
    $started = [DateTime]::UtcNow
    & sqlcmd -S localhost -d master -E -b -r 1 -i (Join-Path $repo 'TraderDB\Operations\Backup-TraderDB.sql') -o (Join-Path $evidence 'backup.log')
    if ($LASTEXITCODE -ne 0) { throw 'Backup or checksum verification failed; no migration ran.' }
    $backupJson = & sqlcmd -S localhost -d master -E -b -y 0 -w 65535 -Q "SET NOCOUNT ON; SELECT TOP(1) m.physical_device_name BackupFile,b.has_backup_checksums Checksums,b.is_damaged Damaged,b.compressed_backup_size Bytes FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily m ON m.media_set_id=b.media_set_id WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.backup_finish_date>=DATEADD(minute,-30,GETDATE()) ORDER BY b.backup_finish_date DESC FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"
    if ($LASTEXITCODE -ne 0) { throw 'Backup metadata verification failed.' }
    $backup = (($backupJson | Where-Object { $_.Trim().StartsWith('{') }) -join '') | ConvertFrom-Json
    if (!$backup.Checksums -or $backup.Damaged -or !(Test-Path -LiteralPath $backup.BackupFile -PathType Leaf)) { throw 'Backup verification evidence is incomplete.' }
    $destination = $env:TRADERVI_BACKUP_DESTINATION_DIRECTORY
    if (!$destination) {
        if (!$env:OneDrive) { throw 'Established backup destination is unavailable; retain the staging backup.' }
        $destination = Join-Path $env:OneDrive 'Joseph\Tradervi\backups'
    }
    if (!(Test-Path -LiteralPath $destination -PathType Container)) { throw 'Established backup destination is missing.' }
    $copy = Join-Path $destination ([IO.Path]::GetFileName($backup.BackupFile))
    if (Test-Path -LiteralPath $copy) { throw 'Backup copy already exists; never overwrite.' }
    [IO.File]::Copy($backup.BackupFile,$copy,$false)
    $hash = (Get-FileHash -LiteralPath $backup.BackupFile -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $hash) { throw 'Backup copy checksum differs.' }
    [pscustomobject]@{StartedUtc=$started;VerifiedUtc=[DateTime]::UtcNow;BackupFile=$backup.BackupFile;CopyFile=$copy;Sha256=$hash;Bytes=$backup.Bytes} |
        ConvertTo-Json | Set-Content -LiteralPath $backupRecord
    Write-Output 'Checksum full backup and hash-matched copy completed. Cloud synchronization is a separate check. No cleanup ran.'
    exit 0
}
if (!(Test-Path -LiteralPath $backupRecord)) { throw 'A verified backup receipt is required.' }
$backup = Get-Content -LiteralPath $backupRecord -Raw | ConvertFrom-Json
$syncRecord = Join-Path $evidence 'backup-sync.json'
if (!(Test-Path -LiteralPath $syncRecord)) { throw 'Confirm synchronization of the verified backup copy first.' }
$sync = Get-Content -LiteralPath $syncRecord -Raw | ConvertFrom-Json
if (!$sync.InSync -or $sync.BackupSha256 -ne $backup.Sha256) { throw 'The exact verified backup copy is not confirmed synchronized.' }
if ([DateTime]::UtcNow - ([DateTime]$backup.VerifiedUtc).ToUniversalTime() -gt [TimeSpan]::FromHours(2)) { throw 'Backup freshness expired; review a new backup.' }
if ((Get-FileHash -LiteralPath $backup.BackupFile -Algorithm SHA256).Hash -ne $backup.Sha256) { throw 'Verified backup bytes changed.' }
$script = @(Get-ChildItem -LiteralPath (Join-Path $repo 'TraderDB\Migrations') -File | Where-Object { $_.Name -like "20261022_${Step}_*.sql" })
if ($script.Count -ne 1) { throw 'Expected exactly one reviewed migration.' }
$receipt = Join-Path $evidence "migration-$Step.json"
if (Test-Path -LiteralPath $receipt) { throw 'A migration receipt exists; do not replay it.' }
$hash = (Get-FileHash -LiteralPath $script[0].FullName -Algorithm SHA256).Hash
# SQLCMD imports process environment variables; avoid native -v tokenization of Windows drive paths.
if ($backup.BackupFile.Contains("'") -or $backup.BackupFile.Contains("`n") -or $backup.BackupFile.Contains("`r")) { throw 'Unsupported backup path.' }
$priorBackupVariable = [Environment]::GetEnvironmentVariable('VerifiedBackupFile', 'Process')
try {
    [Environment]::SetEnvironmentVariable('VerifiedBackupFile', $backup.BackupFile, 'Process')
    & sqlcmd -S localhost -d TraderDB -E -b -r 1 -i $script[0].FullName -o (Join-Path $evidence "migration-$Step.log")
    if ($LASTEXITCODE -ne 0) { throw "Migration $Step failed. Preserve its log and inspect schema; do not replay blindly." }
} finally { [Environment]::SetEnvironmentVariable('VerifiedBackupFile', $priorBackupVariable, 'Process') }
[pscustomobject]@{Migration=$script[0].Name;Sha256=$hash;AppliedUtc=[DateTime]::UtcNow;BackupSha256=$backup.Sha256} |
    ConvertTo-Json | Set-Content -LiteralPath $receipt
Write-Output "Migration $Step completed with its committed preconditions/postconditions. Verify preservation separately."
