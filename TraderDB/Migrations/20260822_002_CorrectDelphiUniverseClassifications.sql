/*
Purpose: Correct the 19 symbol-classification gaps reported by Delphi on
         2026-08-22.
Expected precondition: All 19 audited dbo.Symbols rows exist and still have
         either their reviewed original values or the intended values below.
         dbo.StockSectorMap.GDI is absent, Unknown/unmapped, or already has
         the intended Industrials mapping.
Data effect: Reclassifies 14 active funds from Stock to ETF; marks four
         terminated TSX listings inactive; inserts or corrects GDI's
         stock-sector mapping. No rows or price history are deleted.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         rollback is required.

Classification evidence reviewed 2026-08-22:
  BIGY  https://evolveetfs.com/product/bigy/
  BTCC  https://www.purposeinvest.com/funds/purpose-bitcoin-etf
  CRCY  https://harvestportfolios.com/high-income-shares/crcy/
  ENCL  https://www.globalx.ca/product/encl
  FBTC  https://www.fidelity.ca/en/products/etfs/fbtc/
  FIE   https://www.blackrock.com/ca/investors/en/products/239476/
  JEPQ  https://am.jpmorgan.com/ca/en/asset-management/adv/products/
        jpmorgan-nasdaq-equity-premium-income-active-etf-etf-shares-48129q107
  PMIF  https://www.pimco.com/ca/en/investment-strategies/income-strategies
  PSA   https://www.purposeinvest.com/funds/purpose-high-interest-savings-fund/
        performance
  RBNK  https://www.rbcgam.com/en/ca/products/etfs/index
  RDDY  https://harvestportfolios.com/high-income-shares/rddy/
  SIXY  https://evolveetfs.com/ultrayield/
  SOFY  https://harvestportfolios.com/high-income-shares/sofy/
  TEC   https://www.td.com/ca/en/asset-management/funds/solutions/etfs/fundcard
  BITF  https://investor.bitfarms.com/news-releases/news-release-details/
        bitfarms-officially-rebrands-keel-infrastructure-completes-us
  GLXY  https://investor.galaxy.com/news-releases/news-release-details/
        galaxy-voluntarily-delist-tsx-favor-its-current-nasdaq-listing
  NGD   https://newgold.com/news-events/news/default.aspx
  NVA   https://investor.ovintiv.com/2026-02-03-Ovintiv-Announces-Closing-of-
        NuVista-Energy-Acquisition
  GDI   https://gdi.com/about-us/
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 51200, 'Expected table dbo.Symbols does not exist.', 1;

    IF OBJECT_ID(N'dbo.StockSectorMap', N'U') IS NULL
        THROW 51201, 'Expected table dbo.StockSectorMap does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.StockSectorMap', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.StockSectorMap', N'Sector') IS NULL
       OR COL_LENGTH(N'dbo.StockSectorMap', N'Industry') IS NULL
       OR COL_LENGTH(N'dbo.StockSectorMap', N'SectorIndexSymbol') IS NULL
       OR COL_LENGTH(N'dbo.StockSectorMap', N'LastUpdated') IS NULL
        THROW 51202, 'Classification tables do not have the expected columns.', 1;

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
        THROW 51203, 'A checksum full backup completed within the last 24 hours is required.', 1;

    DECLARE @DesiredSymbols TABLE
    (
        Symbol nvarchar(20) NOT NULL PRIMARY KEY,
        DesiredSecurityType nvarchar(20) NOT NULL,
        DesiredIsActive bit NOT NULL,
        AllowedOriginalSecurityType nvarchar(20) NOT NULL,
        AllowedOriginalIsActive bit NOT NULL
    );

    INSERT @DesiredSymbols
        (Symbol, DesiredSecurityType, DesiredIsActive,
         AllowedOriginalSecurityType, AllowedOriginalIsActive)
    VALUES
        (N'BIGY', N'ETF',   1, N'Stock', 1),
        (N'BTCC', N'ETF',   1, N'Stock', 1),
        (N'CRCY', N'ETF',   1, N'Stock', 1),
        (N'ENCL', N'ETF',   1, N'Stock', 1),
        (N'FBTC', N'ETF',   1, N'Stock', 1),
        (N'FIE',  N'ETF',   1, N'Stock', 1),
        (N'JEPQ', N'ETF',   1, N'Stock', 1),
        (N'PMIF', N'ETF',   1, N'Stock', 1),
        (N'PSA',  N'ETF',   1, N'Stock', 1),
        (N'RBNK', N'ETF',   1, N'Stock', 1),
        (N'RDDY', N'ETF',   1, N'Stock', 1),
        (N'SIXY', N'ETF',   1, N'Stock', 1),
        (N'SOFY', N'ETF',   1, N'Stock', 1),
        (N'TEC',  N'ETF',   1, N'Stock', 1),
        (N'BITF', N'Stock', 0, N'Stock', 1),
        (N'GLXY', N'Stock', 0, N'Stock', 1),
        (N'NGD',  N'Stock', 0, N'Stock', 1),
        (N'NVA',  N'Stock', 0, N'Stock', 1),
        (N'GDI',  N'Stock', 1, N'Stock', 1);

    IF (SELECT COUNT(*) FROM @DesiredSymbols) <> 19
        THROW 51204, 'The reviewed symbol set is incomplete.', 1;

    IF EXISTS
    (
        SELECT desired.Symbol
        FROM @DesiredSymbols AS desired
        LEFT JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = desired.Symbol
        WHERE actual.Symbol IS NULL
    )
        THROW 51205, 'One or more reviewed symbols are missing from dbo.Symbols.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM @DesiredSymbols AS desired
        INNER JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = desired.Symbol
        WHERE NOT
        (
            (actual.SecurityType = desired.AllowedOriginalSecurityType
             AND actual.IsActive = desired.AllowedOriginalIsActive)
            OR
            (actual.SecurityType = desired.DesiredSecurityType
             AND actual.IsActive = desired.DesiredIsActive)
        )
    )
        THROW 51206, 'A reviewed symbol changed after the audit; no corrections were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.StockSectorMap WITH (UPDLOCK, HOLDLOCK)
        WHERE Symbol = N'GDI'
          AND NOT
          (
              (Sector = N'Unknown'
               AND Industry IS NULL
               AND SectorIndexSymbol IS NULL)
              OR
              (Sector = N'Industrials'
               AND Industry = N'Integrated Facility Services'
               AND SectorIndexSymbol = N'^TTIN')
          )
    )
        THROW 51207, 'GDI has an unreviewed sector mapping; no corrections were applied.', 1;

    UPDATE actual
    SET
        SecurityType = desired.DesiredSecurityType,
        IsActive = desired.DesiredIsActive
    FROM dbo.Symbols AS actual
    INNER JOIN @DesiredSymbols AS desired ON desired.Symbol = actual.Symbol
    WHERE actual.SecurityType <> desired.DesiredSecurityType
       OR actual.IsActive <> desired.DesiredIsActive;

    DECLARE @symbolRowsUpdated int = @@ROWCOUNT;

    IF EXISTS (SELECT 1 FROM dbo.StockSectorMap WHERE Symbol = N'GDI')
    BEGIN
        UPDATE dbo.StockSectorMap
        SET
            Sector = N'Industrials',
            Industry = N'Integrated Facility Services',
            SectorIndexSymbol = N'^TTIN',
            LastUpdated = SYSUTCDATETIME()
        WHERE Symbol = N'GDI'
          AND
          (
              Sector <> N'Industrials'
              OR Industry IS NULL
              OR Industry <> N'Integrated Facility Services'
              OR SectorIndexSymbol IS NULL
              OR SectorIndexSymbol <> N'^TTIN'
          );
    END
    ELSE
    BEGIN
        INSERT dbo.StockSectorMap
            (Symbol, Sector, Industry, SectorIndexSymbol, LastUpdated)
        VALUES
            (N'GDI', N'Industrials', N'Integrated Facility Services',
             N'^TTIN', SYSUTCDATETIME());
    END;

    DECLARE @sectorRowsChanged int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM @DesiredSymbols AS desired
        INNER JOIN dbo.Symbols AS actual ON actual.Symbol = desired.Symbol
        WHERE actual.SecurityType <> desired.DesiredSecurityType
           OR actual.IsActive <> desired.DesiredIsActive
    )
        THROW 51208, 'Symbol-classification postcondition failed; transaction will be rolled back.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.StockSectorMap
        WHERE Symbol = N'GDI'
          AND Sector = N'Industrials'
          AND Industry = N'Integrated Facility Services'
          AND SectorIndexSymbol = N'^TTIN'
    )
        THROW 51209, 'GDI sector-mapping postcondition failed; transaction will be rolled back.', 1;

    COMMIT TRANSACTION;

    SELECT
        @symbolRowsUpdated AS SymbolRowsUpdated,
        @sectorRowsChanged AS SectorRowsInsertedOrUpdated;

    SELECT
        actual.Symbol,
        actual.SecurityType,
        actual.IsActive
    FROM dbo.Symbols AS actual
    INNER JOIN @DesiredSymbols AS desired ON desired.Symbol = actual.Symbol
    ORDER BY actual.Symbol;

    SELECT
        Symbol,
        Sector,
        Industry,
        SectorIndexSymbol,
        LastUpdated
    FROM dbo.StockSectorMap
    WHERE Symbol = N'GDI';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
