/*
Purpose: Retire ARX and SES from the active collection/recommendation universe
         after completed acquisitions; their unavailable TMX sector lookups
         blocked the 2026-09-17 nightly pipeline.
Preconditions: Exact reviewed Stock identities and latest price dates below;
         a checksum full backup completed within the last 24 hours.
Data effect: Only dbo.Symbols.IsActive becomes 0 for ARX and SES. All price,
         sector, recommendation and trading history remains unchanged.
Recovery: Any failed guard rolls back the transaction. After commit, a reviewed
         inverse update can restore only these two IsActive flags if needed.
         Do not restore the whole database over newer operational work.

Issuer evidence reviewed 2026-09-17:
ARX: Shell acquisition closed September 2; TSX delisting expected at close
     two trading days later (September 4).
https://www.arcresources.com/news-releases/arc-resources-ltd-announces-closing-of-acquisition-by-shell-plc/
SES: GFL acquisition closed September 1; TSX delisting expected September 2.
https://investors.gflenv.com/English/news/news-details/2026/GFL-Environmental-Closes-Acquisition-of-SECURE-Waste-Infrastructure-Corp-/default.aspx
Both TMX quote queries returned code 404 on September 17. Stored price history
ends on the respective delisting dates. This is a reviewed data correction,
not an automatic rule that treats provider errors as delistings.
*/
USE [TraderDB];
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
       OR OBJECT_ID(N'dbo.DailyBars', N'U') IS NULL
       OR OBJECT_ID(N'dbo.StockSectorMap', N'U') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
        THROW 53600, 'Expected symbol, price and sector schema is missing.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM msdb.dbo.backupset
        WHERE database_name = N'TraderDB' AND [type] = N'D'
          AND backup_finish_date >= DATEADD(DAY, -1, SYSDATETIME())
          AND has_backup_checksums = 1 AND is_damaged = 0
    )
        THROW 53601, 'A recent checksum full backup is required.', 1;

    DECLARE @reviewed TABLE
    (
        Symbol nvarchar(20) PRIMARY KEY,
        ShortName nvarchar(100) NOT NULL,
        LastPriceDate date NOT NULL,
        PriceRows bigint NOT NULL
    );
    INSERT @reviewed VALUES
        (N'ARX', N'ARC Resources Ltd.', '20260904', 1677),
        (N'SES', N'Secure Waste Inf Crp', '20260902', 1675);

    IF EXISTS
    (
        SELECT 1 FROM @reviewed r
        LEFT JOIN dbo.Symbols s WITH (UPDLOCK, HOLDLOCK) ON s.Symbol = r.Symbol
        WHERE s.Symbol IS NULL OR s.SecurityType <> N'Stock'
           OR s.ShortName IS NULL OR s.ShortName <> r.ShortName
    )
        THROW 53602, 'Reviewed symbol identity changed; no changes applied.', 1;

    IF EXISTS
    (
        SELECT 1 FROM @reviewed r
        OUTER APPLY
        (
            SELECT MAX(b.[Date]) AS LastPriceDate, COUNT_BIG(*) AS PriceRows
            FROM dbo.DailyBars b WHERE b.Symbol = r.Symbol
        ) p
        WHERE p.LastPriceDate IS NULL OR p.LastPriceDate <> r.LastPriceDate
           OR p.PriceRows <> r.PriceRows
    )
        THROW 53603, 'Reviewed price history changed; no changes applied.', 1;

    SELECT m.* INTO #SectorBefore
    FROM dbo.StockSectorMap m JOIN @reviewed r ON r.Symbol = m.Symbol;

    UPDATE s SET IsActive = 0
    FROM dbo.Symbols s JOIN @reviewed r ON r.Symbol = s.Symbol
    WHERE s.IsActive = 1;
    DECLARE @changed int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1 FROM @reviewed r JOIN dbo.Symbols s ON s.Symbol = r.Symbol
        WHERE s.IsActive <> 0
    ) OR EXISTS
    (
        SELECT 1 FROM @reviewed r
        WHERE (SELECT COUNT_BIG(*) FROM dbo.DailyBars b WHERE b.Symbol = r.Symbol) <> r.PriceRows
    ) OR EXISTS
    (
        SELECT * FROM #SectorBefore
        EXCEPT SELECT m.* FROM dbo.StockSectorMap m JOIN @reviewed r ON r.Symbol = m.Symbol
    ) OR EXISTS
    (
        SELECT m.* FROM dbo.StockSectorMap m JOIN @reviewed r ON r.Symbol = m.Symbol
        EXCEPT SELECT * FROM #SectorBefore
    )
        THROW 53604, 'Deactivation or history-preservation postcondition failed.', 1;

    COMMIT TRANSACTION;
    SELECT @changed AS SymbolRowsUpdated;
    SELECT s.Symbol, s.IsActive, r.PriceRows AS PriceRowsPreserved, r.LastPriceDate
    FROM dbo.Symbols s JOIN @reviewed r ON r.Symbol = s.Symbol ORDER BY s.Symbol;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
