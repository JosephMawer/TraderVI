/*
Purpose: Mark ECN Capital Corp. (ECN) inactive after its common shares ceased
         trading on the TSX following the April 2026 acquisition.
Expected precondition: ECN exists as a Stock, its latest DailyBars row is
         2026-04-28, and a checksum full backup completed within the last day.
Data effect: Sets dbo.Symbols.IsActive to 0 for ECN. No rows, price history,
         sector mappings, or other data are deleted.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         recovery is required.

Official evidence reviewed 2026-08-22:
  ECN Capital announced that its acquisition closed on 2026-04-24 and that
  its common shares would be delisted from the TSX shortly afterward:
  https://www.ecncapitalcorp.com/wp-content/uploads/
  Project-Sinatra-Closing-Press-Release-FINAL-1407-7994-6523-v12.pdf
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 51300, 'Expected table dbo.Symbols does not exist.', 1;

    IF OBJECT_ID(N'dbo.DailyBars', N'U') IS NULL
        THROW 51301, 'Expected table dbo.DailyBars does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Date') IS NULL
        THROW 51302, 'Symbol or price-history tables do not have the expected columns.', 1;

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
        THROW 51303, 'A checksum full backup completed within the last 24 hours is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N'ECN'
    )
        THROW 51304, 'Expected ECN row does not exist; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N'ECN'
          AND SecurityType <> N'Stock'
    )
        THROW 51305, 'ECN is no longer classified as Stock; no changes were applied.', 1;

    DECLARE @latestPriceDate date =
        (SELECT MAX([Date]) FROM dbo.DailyBars WHERE Symbol = N'ECN');

    IF @latestPriceDate IS NULL
       OR @latestPriceDate <> CONVERT(date, '2026-04-28')
        THROW 51306, 'ECN price history changed after review; no changes were applied.', 1;

    UPDATE dbo.Symbols
    SET IsActive = 0
    WHERE Symbol = N'ECN'
      AND IsActive = 1;

    DECLARE @symbolRowsUpdated int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols
        WHERE Symbol = N'ECN'
          AND IsActive <> 0
    )
        THROW 51307, 'ECN deactivation postcondition failed; transaction will be rolled back.', 1;

    COMMIT TRANSACTION;

    SELECT @symbolRowsUpdated AS SymbolRowsUpdated;

    SELECT
        s.Symbol,
        s.SecurityType,
        s.IsActive,
        COUNT_BIG(b.[Date]) AS PriceRowsPreserved,
        MIN(b.[Date]) AS FirstPriceDate,
        MAX(b.[Date]) AS LastPriceDate
    FROM dbo.Symbols AS s
    LEFT JOIN dbo.DailyBars AS b ON b.Symbol = s.Symbol
    WHERE s.Symbol = N'ECN'
    GROUP BY s.Symbol, s.SecurityType, s.IsActive;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
