/*
Purpose: Deactivate the six remaining stale or lagging symbols whose TSX
         listings ended after completed corporate actions in 2026.
Expected precondition: Each reviewed symbol exists as a Stock, has the exact
         latest DailyBars date recorded below, and a checksum full backup
         completed within the last day.
Data effect: Sets dbo.Symbols.IsActive to 0 for BLX, OLA, QIPT, SOY, URC,
         and WNDR. No rows, price history, sector mappings, or other data are
         deleted. Replacement securities are separate onboarding decisions.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         recovery is required.

Official evidence reviewed 2026-08-22:
  BLX   Boralex acquisition completed; TSX delisting expected 2026-08-17:
        https://www.boralex.com/en/press-releases/
        brookfield-and-la-caisse-complete-acquisition-boralex
  OLA   Equinox Gold/Orla combination completed 2026-07-31; Equinox intends
        to delist OLA:
        https://www.equinoxgold.com/news/
        equinox-gold-and-orla-mining-complete-business-combination-creating-
        north-americas-new-senior-gold-producer/
  QIPT  Acquisition completed; TSX delisting at close on 2026-03-17:
        https://www.globenewswire.com/news-release/2026/03/16/3256165/0/en/
        quipt-home-medical-completes-the-previously-announced-arrangement-
        with-affiliates-of-kingswood-and-forager.html
  SOY   Refresco acquisition completed; TSX delisting at close on 2026-05-05:
        https://www.m-x.ca/f_circulaires_en/060-26_en.pdf
  URC   Sweetwater transaction completed; TSX delisting at close on
        2026-07-28:
        https://www.uraniumroyalty.com/news/uranium-royalty-completes-
        landmark-sweetwater-transaction-creating-leading-uranium-and-land-
        royalty-company
  WNDR  Robinhood acquisition completed; TSX delisting expected at close on
        2026-06-02:
        https://www.wonder.fi/press-release/
        robinhood-completes-acquisition-of-wonderfi
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 51700, 'Expected table dbo.Symbols does not exist.', 1;

    IF OBJECT_ID(N'dbo.DailyBars', N'U') IS NULL
        THROW 51701, 'Expected table dbo.DailyBars does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.DailyBars', N'Date') IS NULL
        THROW 51702, 'Symbol or price-history tables do not have the expected columns.', 1;

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
        THROW 51703, 'A checksum full backup completed within the last 24 hours is required.', 1;

    DECLARE @ReviewedSymbols TABLE
    (
        Symbol nvarchar(20) NOT NULL PRIMARY KEY,
        ExpectedLatestPriceDate date NOT NULL
    );

    INSERT @ReviewedSymbols (Symbol, ExpectedLatestPriceDate)
    VALUES
        (N'BLX',  CONVERT(date, '2026-08-17')),
        (N'OLA',  CONVERT(date, '2026-08-04')),
        (N'QIPT', CONVERT(date, '2026-03-17')),
        (N'SOY',  CONVERT(date, '2026-05-05')),
        (N'URC',  CONVERT(date, '2026-07-28')),
        (N'WNDR', CONVERT(date, '2026-06-02'));

    IF (SELECT COUNT(*) FROM @ReviewedSymbols) <> 6
        THROW 51704, 'The reviewed stale-listing set is incomplete.', 1;

    IF EXISTS
    (
        SELECT reviewed.Symbol
        FROM @ReviewedSymbols AS reviewed
        LEFT JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = reviewed.Symbol
        WHERE actual.Symbol IS NULL
    )
        THROW 51705, 'One or more reviewed symbols are missing; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM @ReviewedSymbols AS reviewed
        INNER JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = reviewed.Symbol
        WHERE actual.SecurityType <> N'Stock'
    )
        THROW 51706, 'A reviewed symbol is no longer classified as Stock; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT reviewed.Symbol
        FROM @ReviewedSymbols AS reviewed
        OUTER APPLY
        (
            SELECT MAX(b.[Date]) AS LatestPriceDate
            FROM dbo.DailyBars AS b
            WHERE b.Symbol = reviewed.Symbol
        ) AS prices
        WHERE prices.LatestPriceDate IS NULL
           OR prices.LatestPriceDate <> reviewed.ExpectedLatestPriceDate
    )
        THROW 51707, 'Price history changed for a reviewed symbol; no changes were applied.', 1;

    UPDATE actual
    SET IsActive = 0
    FROM dbo.Symbols AS actual
    INNER JOIN @ReviewedSymbols AS reviewed ON reviewed.Symbol = actual.Symbol
    WHERE actual.IsActive = 1;

    DECLARE @symbolRowsUpdated int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM @ReviewedSymbols AS reviewed
        INNER JOIN dbo.Symbols AS actual ON actual.Symbol = reviewed.Symbol
        WHERE actual.IsActive <> 0
    )
        THROW 51708, 'Stale-listing postcondition failed; transaction will be rolled back.', 1;

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
    INNER JOIN @ReviewedSymbols AS reviewed ON reviewed.Symbol = s.Symbol
    LEFT JOIN dbo.DailyBars AS b ON b.Symbol = s.Symbol
    GROUP BY s.Symbol, s.SecurityType, s.IsActive
    ORDER BY s.Symbol;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
