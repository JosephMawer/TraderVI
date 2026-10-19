param([Parameter(Mandatory)][string]$InstalledDirectory)
$ErrorActionPreference = 'Stop'
$mailbox = 'C:\src\TraderVI\artifacts\read-only-db'
$request = Join-Path $mailbox 'request.sql'
$pwsh = (Get-Process -Id $PID).Path
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [string]$Sql, [bool]$Success, [int[]]$Errors = @(), [string]$LocalError = 'Read-only SQL policy rejected the request.') {
    [IO.File]::WriteAllText($request, $Sql, [Text.UTF8Encoding]::new($false))
    $raw = & $pwsh -NoProfile -File (Join-Path $InstalledDirectory 'Invoke.ps1') 2>&1
    $code = $LASTEXITCODE
    $receipt = ($raw | ForEach-Object { $_.ToString() }) -join "`n" | ConvertFrom-Json
    if ($Success -and ($code -ne 0 -or -not $receipt.Success)) { throw "$Name failed: $raw" }
    if (-not $Success -and ($code -eq 0 -or ($Errors.Count -gt 0 -and $receipt.SqlError -notin $Errors))) { throw "$Name did not reject with the expected SQL permission error: $raw" }
    if (-not $Success -and $Errors.Count -eq 0 -and ($receipt.SqlError -or $receipt.Error -ne $LocalError)) { throw "$Name returned the wrong local error: $raw" }
    $checks.Add([pscustomobject]@{Name=$Name;Passed=$true;SqlError=$receipt.SqlError})
    return $receipt
}
$positive = Check 'SELECT and restricted identity' "SELECT USER_NAME() AS DatabaseUser, COUNT_BIG(*) AS TableCount FROM sys.tables; SELECT COUNT_BIG(*) AS Symbols FROM dbo.Symbols;" $true
$data = Get-Content -LiteralPath $positive.ResultFile -Raw | ConvertFrom-Json
if ($data.ResultSets[0].Rows[0][0] -ne 'TraderVIReadOnlyChecks' -or $positive.ResultSets -ne 2) { throw 'Unexpected result identity or shape.' }
$null = Check 'UPDATE rejected without touching rows' 'UPDATE TOP (0) dbo.Symbols SET IsActive=IsActive;' $false
$null = Check 'DELETE rejected without touching rows' 'DELETE TOP (0) FROM dbo.Symbols;' $false
$null = Check 'INSERT rejected with empty source' "INSERT INTO dbo.Symbols(Symbol) SELECT N'__readonly_probe__' WHERE 1=0;" $false
$null = Check 'REVERT rejected' 'REVERT; SELECT USER_NAME() AS DatabaseUser;' $false
$null = Check 'dbo impersonation rejected' "EXECUTE AS USER='dbo'; SELECT USER_NAME();" $false
$server = Check 'No server control or ALTER ANY DATABASE authority' "SELECT COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,'CONTROL SERVER'),0) AS CanControlServer,COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,'ALTER ANY DATABASE'),0) AS CanAlterDatabases;" $true
$data = Get-Content -LiteralPath $server.ResultFile -Raw | ConvertFrom-Json
if (@($data.ResultSets[0].Rows[0] | Where-Object { $_ -ne 0 }).Count) { throw 'Unexpected server administration authority.' }
$permissions = Check 'No DDL, DML, procedure execution or impersonation authority' "SELECT HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER') AS CanAlter,HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CREATE TABLE') AS CanCreateTable,HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','EXECUTE') AS CanExecute,HAS_PERMS_BY_NAME('dbo','USER','IMPERSONATE') AS CanImpersonate,HAS_PERMS_BY_NAME('dbo.Symbols','OBJECT','INSERT') AS CanInsert,HAS_PERMS_BY_NAME('dbo.Symbols','OBJECT','UPDATE') AS CanUpdate,HAS_PERMS_BY_NAME('dbo.Symbols','OBJECT','DELETE') AS CanDelete;" $true
$data = Get-Content -LiteralPath $permissions.ResultFile -Raw | ConvertFrom-Json
if (@($data.ResultSets[0].Rows[0] | Where-Object { $_ -ne 0 }).Count) { throw 'Unexpected mutation authority.' }
$null = Check 'SQLCMD shell directives are rejected' ':!! whoami' $false
$null = Check 'Row limit rejects oversized results' 'SELECT TOP (10001) 1 AS Value FROM sys.all_objects a CROSS JOIN sys.all_objects b;' $false -LocalError 'Row limit exceeded; narrow the query.'
$null = Check 'Cross-database references rejected before SQL' 'SELECT COUNT_BIG(*) FROM master.sys.databases;' $false
$null = Check 'Public system procedure calls rejected before SQL' "EXEC master.sys.xp_dirtree N'C:\__TraderVI_ReadOnly_Probe_Does_Not_Exist__';" $false
$checks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $mailbox 'verification.json')
[IO.File]::WriteAllText($request, 'SELECT USER_NAME() AS DatabaseUser, SYSUTCDATETIME() AS CheckedUtc;', [Text.UTF8Encoding]::new($false))
[pscustomobject]@{Passed=$checks.Count;ApplicationRowsChanged=0;Evidence='artifacts/read-only-db/verification.json'} | ConvertTo-Json -Compress
