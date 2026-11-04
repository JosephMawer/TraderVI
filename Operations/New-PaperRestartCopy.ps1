# Authorized isolated rehearsal only. Production receives one COPY_ONLY backup, never a restore or DML.
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$token = [guid]::NewGuid().ToString('N')
$run = Join-Path $root "artifacts/paper-rehearsal/$token"
$null = New-Item -ItemType Directory -Path $run
$backup = "C:\ProgramData\TraderVI\Backups\PaperRehearsal_$token.bak"
$tool = Join-Path $env:ProgramFiles 'Microsoft SQL Server/170/Tools/Binn/SqlLocalDB.exe'
if (!(Test-Path -LiteralPath $tool) -or (Test-Path -LiteralPath $backup)) { throw 'Missing LocalDB tooling or backup collision.' }
if ($run.Contains("'")) { throw 'Unsupported workspace path.' }
$manifest = [ordered]@{ Token=$token; Folder=$run; Backup=$backup; Stage='backup'; CreatedUtc=[DateTimeOffset]::UtcNow }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'copy.json')
& sqlcmd -S localhost -E -b -r 1 -l 15 -t 900 -d master -Q "BACKUP DATABASE [TraderDB] TO DISK=N'$backup' WITH COPY_ONLY,COMPRESSION,CHECKSUM,STATS=10;" -o (Join-Path $run 'backup.log')
if ($LASTEXITCODE) { throw "Copy-only backup failed. Private evidence: $run" }
$name = "TVIRestart_$token"
& $tool create $name '17.0' -s
if ($LASTEXITCODE) { throw 'Creating private LocalDB failed.' }
$target = "(localdb)\$name"
function Invoke-CopySql([string]$query, [string]$log, [string]$database='master') {
    & sqlcmd -S $target -E -b -r 1 -l 30 -t 900 -d $database -Q $query -o (Join-Path $run $log)
    if ($LASTEXITCODE) { throw "Isolated SQL step failed: $log" }
}
Invoke-CopySql "IF ISNULL(CONVERT(int,SERVERPROPERTY('IsLocalDB')),0)<>1 OR DB_ID(N'TraderDB') IS NOT NULL THROW 54700,'Require fresh private instance',1; RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;" 'verify.log'
$lines = & sqlcmd -S $target -E -b -l 30 -d master -W -s '|' -h -1 -Q "RESTORE FILELISTONLY FROM DISK=N'$backup';"
if ($LASTEXITCODE) { throw 'Could not inspect backup file mapping.' }
$files = @($lines | ForEach-Object { $p = $_.Split('|'); if ($p.Length -ge 6 -and $p[2].Trim() -in @('D','L')) { [pscustomobject]@{Name=$p[0].Trim();Type=$p[2].Trim();Size=[long]$p[4].Trim()} } })
if ($files.Count -ne 2 -or @($files | Where-Object Type -eq 'D').Count -ne 1) { throw 'Review unexpected database file layout before restore.' }
$space = (Get-PSDrive -Name ([IO.Path]::GetPathRoot($run).Substring(0,1))).Free
if ($space -lt (($files | Measure-Object Size -Sum).Sum + 20GB)) { throw 'Insufficient free space for isolated restore.' }
$moves = @($files | ForEach-Object {
    $logical = $_.Name.Replace("'", "''")
    $extension = if ($_.Type -eq 'D') { 'mdf' } else { 'ldf' }
    "MOVE N'$logical' TO N'$run\copy.$extension'"
}) -join ','
Invoke-CopySql "RESTORE DATABASE [TraderDB] FROM DISK=N'$backup' WITH $moves,CHECKSUM,RECOVERY,STATS=10;" 'restore.log'
Invoke-CopySql "EXEC sys.sp_addextendedproperty @name=N'CompactRehearsal',@value=N'$token';" 'marker.log' 'TraderDB'
$manifest.Stage='restored'
$manifest.Files=$files
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'copy.json')
Write-Output "Isolated copy token: $token"
Write-Output "Evidence folder: $run"
# Retain both backup and copy. Cleanup requires separate authorization.
