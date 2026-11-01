# Run only for an authorized disposable rehearsal. sqlcmd always targets a newly named LocalDB instance.
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$token = [guid]::NewGuid().ToString('N')
$name = "TVIRestart_$token"
$run = Join-Path $root "artifacts/restart-release/$token"
$null = New-Item -ItemType Directory -Path $run
$tool = Join-Path $env:ProgramFiles 'Microsoft SQL Server/170/Tools/Binn/SqlLocalDB.exe'
if (!(Test-Path -LiteralPath $tool)) { throw 'SQL Server LocalDB 17 tooling required.' }
& $tool create $name '17.0' -s
if ($LASTEXITCODE) { throw 'Creating isolated LocalDB failed.' }
$target = "(localdb)\$name"
function Invoke-IsolatedSql([string[]]$arguments) {
    & sqlcmd -S $target -E -b -r 1 -l 30 @arguments
    if ($LASTEXITCODE) { throw 'Isolated sqlcmd step failed.' }
}
$backup = Join-Path $run 'before-compact.bak'
if ($run.Contains("'")) { throw 'Unsupported workspace path.' }
Invoke-IsolatedSql @('-d','master','-Q',"IF ISNULL(CONVERT(int,SERVERPROPERTY('IsLocalDB')),0)<>1 OR DB_ID(N'TraderDB') IS NOT NULL THROW 54700,'Require fresh private instance',1; CREATE DATABASE TraderDB ON PRIMARY (NAME=N'TraderDB', FILENAME=N'$run\rehearsal.mdf') LOG ON (NAME=N'TraderDB_log', FILENAME=N'$run\rehearsal.ldf');")
Invoke-IsolatedSql @('-d','TraderDB','-Q',"EXEC sys.sp_addextendedproperty @name=N'CompactRehearsal', @value=N'$token';")
Invoke-IsolatedSql @('-d','TraderDB','-i',(Join-Path $root 'TraderDB/dbo/Tables/BrokerAccountImportBinding.sql'))
Invoke-IsolatedSql @('-d','master','-Q',"BACKUP DATABASE TraderDB TO DISK=N'$backup' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;")
$previousBackup = $env:VerifiedBackupFile
try {
    $env:VerifiedBackupFile = $backup
    foreach ($migration in '20261030_054_AddCompactTradingCurrentState.sql','20261031_055_AllowExpiredCompactOrderReceipt.sql') {
        Invoke-IsolatedSql @('-d','TraderDB','-i',(Join-Path $root "TraderDB/Migrations/$migration"))
    }
} finally { $env:VerifiedBackupFile = $previousBackup }
Invoke-IsolatedSql @('-d','TraderDB','-Q',"IF (SELECT COUNT(*) FROM sys.tables WHERE name IN ('TradingAccountCurrent','TradingApprovedSettings','TradingPositionCurrent','TradingSymbolCurrent','TradingOrder','TradingRestrictionCurrent','TradingCheckpoint'))<>7 OR ISNULL(OBJECT_DEFINITION(OBJECT_ID('dbo.CK_TradingOrder_2')),'') NOT LIKE '%Expired%' THROW 54701,'Schema verification failed',1;")
Write-Output "Rehearsal token: $token"
