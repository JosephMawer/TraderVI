/*
Purpose: Mark Goodfood Market Corp. (FOOD) inactive while its TSX common
         shares are suspended from trading and under expedited delisting
         review.
Expected precondition: FOOD exists as a Stock, its latest DailyBars row is
         2026-08-04, and a checksum full backup completed within the last day.
Data effect: Sets dbo.Symbols.IsActive to 0 for FOOD. No rows, price history,
         sector mappings, or other data are deleted. FOOD can be reviewed and
         reactivated separately if TSX trading later resumes.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         recovery is required.

Official evidence reviewed 2026-08-22:
  TSX suspended FOOD and FOOD.DB.A immediately on 2026-08-05 and began an
  expedited delisting review:
  https://www.tsx.com/en/news/reviews-and-suspensions?id=819

  Goodfood announced that it obtained an initial order under the Companies'
  Creditors Arrangement Act on 2026-08-05:
  https://www2.makegoodfood.ca/en/investisseurs/communiques
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 51400, 'Expected table dbo.Symbols does not exist.', 1;

    IF OBJECT_ID(N'dbo.DailyBars', N'U') IS NULL
        THROW 51401, 'Expected table dbo.DailyBars does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Date') IS NULL
        THROW 51402, 'Symbol or price-history tables do not have the expected columns.', 1;

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
        THROW 51403, 'A checksum full backup completed within the last 24 hours is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N'FOOD'
    )
        THROW 51404, 'Expected FOOD row does not exist; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N'FOOD'
          AND SecurityType <> N'Stock'
    )
        THROW 51405, 'FOOD is no longer classified as Stock; no changes were applied.', 1;

    DECLARE @latestPriceDate date =
        (SELECT MAX([Date]) FROM dbo.DailyBars WHERE Symbol = N'FOOD');

    IF @latestPriceDate IS NULL
       OR @latestPriceDate <> CONVERT(date, '2026-08-04')
        THROW 51406, 'FOOD price history changed after review; no changes were applied.', 1;

    UPDATE dbo.Symbols
    SET IsActive = 0
    WHERE Symbol = N'FOOD'
      AND IsActive = 1;

    DECLARE @symbolRowsUpdated int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Symbols
        WHERE Symbol = N'FOOD'
          AND IsActive <> 0
    )
        THROW 51407, 'FOOD deactivation postcondition failed; transaction will be rolled back.', 1;

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
    WHERE s.Symbol = N'FOOD'
    GROUP BY s.Symbol, s.SecurityType, s.IsActive;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
