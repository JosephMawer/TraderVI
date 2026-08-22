/*
Purpose: Remove the single invalid metadata row whose primary-key Symbol is
         the empty string.
Expected precondition: Exactly one dbo.Symbols row has Symbol = N'', it is an
         active Stock with no name, no other user table contains a row that
         references the empty symbol, and a checksum full backup completed
         within the last day.
Data effect: Permanently deletes exactly one invalid dbo.Symbols metadata row.
         No price history, sector mapping, trading record, or other dependent
         data is deleted.
Authorization: The exact deletion was explicitly authorized by the user on
         2026-08-22 after a read-only dependency audit found no related rows.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         recovery is required.
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 51800, 'Expected table dbo.Symbols does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'LongName') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'ShortName') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
        THROW 51801, 'dbo.Symbols does not have the expected columns.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM msdb.dbo.backupset
        WHERE database_name = N'TraderDB'
          AND [type] = N'D'
          AND backup_finish_date >= DATEADD(DAY, -1, SYSDATETIME())
          AND has_backup_checksums = 1
          AND is_damaged = 0
    )
        THROW 51802, 'A checksum full backup completed within the last 24 hours is required.', 1;

    IF
    (
        SELECT COUNT_BIG(*)
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N''
    ) <> 1
        THROW 51803, 'Expected exactly one blank symbol row; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N''
          AND
          (
              SecurityType <> N'Stock'
              OR IsActive <> 1
              OR COALESCE(LongName, N'') <> N''
              OR COALESCE(ShortName, N'') <> N''
          )
    )
        THROW 51804, 'The blank symbol row changed after review; no changes were applied.', 1;

    DECLARE @dependencySql nvarchar(max);
    DECLARE @dependentRowCount bigint = 0;

    SELECT @dependencySql = STRING_AGG
    (
        CAST
        (
            N'SELECT @rowCount += COUNT_BIG(*) FROM '
            + QUOTENAME(schemaInfo.[name]) + N'.' + QUOTENAME(tableInfo.[name])
            + N' WITH (HOLDLOCK) WHERE ' + QUOTENAME(columnInfo.[name]) + N' = N'''';'
            AS nvarchar(max)
        ),
        NCHAR(13) + NCHAR(10)
    )
    FROM sys.tables AS tableInfo
    INNER JOIN sys.schemas AS schemaInfo
        ON schemaInfo.schema_id = tableInfo.schema_id
    INNER JOIN sys.columns AS columnInfo
        ON columnInfo.object_id = tableInfo.object_id
    WHERE columnInfo.[name] = N'Symbol'
      AND tableInfo.object_id <> OBJECT_ID(N'dbo.Symbols', N'U');

    IF NULLIF(@dependencySql, N'') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql
            @dependencySql,
            N'@rowCount bigint OUTPUT',
            @rowCount = @dependentRowCount OUTPUT;
    END;

    IF @dependentRowCount <> 0
        THROW 51805, 'One or more rows reference the blank symbol; no changes were applied.', 1;

    DELETE FROM dbo.Symbols
    WHERE Symbol = N'';

    DECLARE @symbolRowsDeleted int = @@ROWCOUNT;

    IF @symbolRowsDeleted <> 1
        THROW 51806, 'The blank symbol delete affected an unexpected number of rows; transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols
        WHERE Symbol = N''
    )
        THROW 51807, 'Blank symbol postcondition failed; transaction will be rolled back.', 1;

    COMMIT TRANSACTION;

    SELECT @symbolRowsDeleted AS SymbolRowsDeleted;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
