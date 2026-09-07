-- Read-only preservation check around a model-selection transition. Output is
-- row counts and aggregate checksums, never market/trading rows or model payloads.
USE [TraderDB];
SET NOCOUNT ON;
DECLARE @queries nvarchar(max);
SELECT @queries=STRING_AGG(CONVERT(nvarchar(max),N'SELECT N'''+REPLACE(t.name,'''','''''')+
    N''' AS TableName,COUNT_BIG(*) AS [RowCount],CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS RowChecksum FROM '+QUOTENAME(SCHEMA_NAME(t.schema_id))+N'.'+QUOTENAME(t.name)),N' UNION ALL ')
FROM sys.tables t
WHERE t.name LIKE N'Calibration%' OR t.name LIKE N'DelphiLive%' OR t.name LIKE N'Shadow%'
   OR t.name LIKE N'SystemShadow%' OR t.name IN (N'DailyPick',N'DecisionDossier',N'Trade',N'TrackedPosition');
IF @queries IS NULL THROW 51281, 'No expected evidence tables found.', 1;
EXEC(N'SELECT * FROM ('+@queries+N') snapshots ORDER BY TableName FOR JSON PATH, INCLUDE_NULL_VALUES;');
