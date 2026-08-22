/*
Purpose: Mark GDI Integrated Facility Services Inc. (GDI) inactive after its
         March 2026 take-private transaction and TSX delisting.
Expected precondition: GDI exists as a Stock, its latest DailyBars row is
         2026-03-03, and a checksum full backup completed within the last day.
Data effect: Sets dbo.Symbols.IsActive to 0 for GDI. No rows, price history,
         sector mappings, or other data are deleted. The reviewed historical
         Industrials mapping is intentionally retained.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         recovery is required.

Official evidence reviewed 2026-08-22:
  GDI announced that its take-private arrangement completed on 2026-03-02
  and that its subordinate voting shares were expected to be delisted at the
  close of business on 2026-03-03:
  https://www.newswire.ca/news-releases/
  gdi-completes-previously-announced-plan-of-arrangement-833698224.html
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 51500, 'Expected table dbo.Symbols does not exist.', 1;

    IF OBJECT_ID(N'dbo.DailyBars', N'U') IS NULL
        THROW 51501, 'Expected table dbo.DailyBars does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Date') IS NULL
        THROW 51502, 'Symbol or price-history tables do not have the expected columns.', 1;

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
        THROW 51503, 'A checksum full backup completed within the last 24 hours is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N'GDI'
    )
        THROW 51504, 'Expected GDI row does not exist; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N'GDI'
          AND SecurityType <> N'Stock'
    )
        THROW 51505, 'GDI is no longer classified as Stock; no changes were applied.', 1;

    DECLARE @latestPriceDate date =
        (SELECT MAX([Date]) FROM dbo.DailyBars WHERE Symbol = N'GDI');

    IF @latestPriceDate IS NULL
       OR @latestPriceDate <> CONVERT(date, '2026-03-03')
        THROW 51506, 'GDI price history changed after review; no changes were applied.', 1;

    UPDATE dbo.Symbols
    SET IsActive = 0
    WHERE Symbol = N'GDI'
      AND IsActive = 1;

    DECLARE @symbolRowsUpdated int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols
        WHERE Symbol = N'GDI'
          AND IsActive <> 0
    )
        THROW 51507, 'GDI deactivation postcondition failed; transaction will be rolled back.', 1;

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
    WHERE s.Symbol = N'GDI'
    GROUP BY s.Symbol, s.SecurityType, s.IsActive;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
