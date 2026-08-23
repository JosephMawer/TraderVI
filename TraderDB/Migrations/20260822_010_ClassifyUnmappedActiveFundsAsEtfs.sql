/*
Purpose: Correct the 39 active, Unknown-sector rows identified by the full
         local data audit that are exchange-traded funds stored as Stock.
Expected precondition: Every reviewed symbol exists as an active Stock or the
         intended active ETF and retains its audited leveraged/inverse flag.
         A checksum full backup completed within the last day.
Data effect: Sets SecurityType to ETF for all 39 reviewed symbols. IsActive,
         IsLeveragedOrInverseEtp, names, sector mappings, and price history are
         preserved unchanged. No rows are inserted or deleted.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         recovery is required.

Classification evidence reviewed 2026-08-22:
  Global X and BetaPro product records cover BITI, CNDD, CNDU, CPCC, GDXD,
  GDXU, GLCC, NRGU, QQD, QQQD, QQU, SLVU, SPXD, SPXU, and USCL:
        https://www.globalx.ca/products
        https://www.globalx.ca/wp-content/uploads/2025/08/
        BetaPro-Prospectus-EN.pdf
  TSX listing bulletins identify the newer Harvest, Evolve, First Trust, and
  Hamilton rows as ETFs, including BLKY, CRWY, INTY, JPHE, NOVY, ORCY, RCTR,
  and UMVP:
        https://www.tsx.com/en/news/new-company-listings?month=1&year=2026
  Additional official issuer records reviewed:
    CCOE, Harvest product catalogue:
        https://harvestportfolios.com/high-income-shares/products/
    CDZ, iShares product page:
        https://www.blackrock.com/ca/investors/en/products/239834/
        ishares-sptsx-canadian-dividend-aristocrats-index-fund
    CGXF, NXF, TXF, CI exchange-traded funds:
        https://funds.cifinancial.com/en/funds/ETFS/
        CIGoldGiantsCoveredCallETF.html
    DMQC, Desjardins product page:
        https://www.fondsdesjardins.com/etf/quebec-equity/
    DXP, Dynamic product page:
        https://fund.dynamic.ca/etf-profile?profileId=DXP
    ETHH, SOLL, Purpose product and financial records:
        https://www.purposeinvest.com/funds/purpose-ether-etf
    INTY, TECH, Evolve product pages:
        https://evolveetfs.com/product/inty/
        https://evolveetfs.com/product/tech/
    MFT, MUB, Mackenzie ETF list:
        https://www.mackenzieinvestments.com/en/investments/by-type/etfs/
        etfs-list
    MIX, UMVP, Hamilton product pages:
        https://hamiltonetfs.com/etf/mix/
        https://hamiltonetfs.com/etf/umvp/
    MSTU, TSLU, LongPoint leveraged ETF catalogue:
        https://www.longpointetfs.com/leveraged
    PDC, Invesco ETF record:
        https://www.invesco.com/ca-en/exchange-traded-funds
    RCTR, First Trust/TSX listing bulletin:
        https://www.tsx.com/en/news/new-company-listings?id=2255
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 52000, 'Expected table dbo.Symbols does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsLeveragedOrInverseEtp') IS NULL
        THROW 52001, 'dbo.Symbols does not have the expected classification columns.', 1;

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
        THROW 52002, 'A checksum full backup completed within the last 24 hours is required.', 1;

    DECLARE @ReviewedFunds TABLE
    (
        Symbol nvarchar(20) NOT NULL PRIMARY KEY,
        ExpectedLeveragedOrInverse bit NOT NULL
    );

    INSERT @ReviewedFunds (Symbol, ExpectedLeveragedOrInverse)
    VALUES
        (N'BITI', 1),
        (N'BLKY', 0),
        (N'CCOE', 0),
        (N'CDZ',  0),
        (N'CGXF', 0),
        (N'CNDD', 1),
        (N'CNDU', 1),
        (N'CPCC', 0),
        (N'CRWY', 0),
        (N'DMQC', 0),
        (N'DXP',  0),
        (N'ETHH', 0),
        (N'GDXD', 1),
        (N'GDXU', 1),
        (N'GLCC', 0),
        (N'INTY', 0),
        (N'JPHE', 0),
        (N'MFT',  0),
        (N'MIX',  0),
        (N'MSTU', 1),
        (N'MUB',  0),
        (N'NOVY', 0),
        (N'NRGU', 1),
        (N'NXF',  0),
        (N'ORCY', 0),
        (N'PDC',  0),
        (N'QQD',  1),
        (N'QQQD', 1),
        (N'QQU',  1),
        (N'RCTR', 0),
        (N'SLVU', 1),
        (N'SOLL', 0),
        (N'SPXD', 1),
        (N'SPXU', 1),
        (N'TECH', 0),
        (N'TSLU', 1),
        (N'TXF',  0),
        (N'UMVP', 0),
        (N'USCL', 0);

    IF (SELECT COUNT(*) FROM @ReviewedFunds) <> 39
        THROW 52003, 'The reviewed active-fund set is incomplete.', 1;

    IF EXISTS
    (
        SELECT reviewed.Symbol
        FROM @ReviewedFunds AS reviewed
        LEFT JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = reviewed.Symbol
        WHERE actual.Symbol IS NULL
    )
        THROW 52004, 'One or more reviewed funds are missing; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM @ReviewedFunds AS reviewed
        INNER JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = reviewed.Symbol
        WHERE actual.IsActive <> 1
           OR actual.SecurityType NOT IN (N'Stock', N'ETF')
           OR actual.IsLeveragedOrInverseEtp <> reviewed.ExpectedLeveragedOrInverse
    )
        THROW 52005, 'A reviewed fund changed after the audit; no changes were applied.', 1;

    UPDATE actual
    SET SecurityType = N'ETF'
    FROM dbo.Symbols AS actual
    INNER JOIN @ReviewedFunds AS reviewed ON reviewed.Symbol = actual.Symbol
    WHERE actual.SecurityType = N'Stock';

    DECLARE @symbolRowsUpdated int = @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM @ReviewedFunds AS reviewed
        INNER JOIN dbo.Symbols AS actual ON actual.Symbol = reviewed.Symbol
        WHERE actual.SecurityType <> N'ETF'
           OR actual.IsActive <> 1
           OR actual.IsLeveragedOrInverseEtp <> reviewed.ExpectedLeveragedOrInverse
    )
        THROW 52006, 'Active-fund classification postcondition failed; transaction will be rolled back.', 1;

    COMMIT TRANSACTION;

    SELECT @symbolRowsUpdated AS SymbolRowsUpdated;

    SELECT
        actual.Symbol,
        actual.SecurityType,
        actual.IsActive,
        actual.IsLeveragedOrInverseEtp
    FROM dbo.Symbols AS actual
    INNER JOIN @ReviewedFunds AS reviewed ON reviewed.Symbol = actual.Symbol
    ORDER BY actual.Symbol;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
