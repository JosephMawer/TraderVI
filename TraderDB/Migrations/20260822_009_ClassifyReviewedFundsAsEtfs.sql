/*
Purpose: Correct the 49 fund-like rows identified by the full local data audit
         that are exchange-traded funds but are stored as Stock.
Expected precondition: Every reviewed symbol exists, retains its audited
         active status, and has SecurityType Stock or the intended ETF value.
         A checksum full backup completed within the last day.
Data effect: Sets SecurityType to ETF for all 49 reviewed symbols. IsActive,
         IsLeveragedOrInverseEtp, names, sector mappings, and price history are
         preserved unchanged. No rows are inserted or deleted.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the recent verified backup if
         recovery is required.

Classification evidence reviewed 2026-08-22:
  JEPI  J.P. Morgan identifies JEPI as its US Equity Premium Income Active ETF:
        https://am.jpmorgan.com/ca/en/asset-management/adv/products/
        jpmorgan-us-equity-premium-income-active-etf-etf-shares-480915107
  LLHE, MSHE
        Harvest's official product suite identifies both as Enhanced High
        Income Shares ETFs:
        https://harvestportfolios.com/high-income-shares/products/
  MSTE  Harvest identifies MSTE as its Strategy Inc. Enhanced High Income
        Shares ETF:
        https://harvestportfolios.com/high-income-shares/mste/
  CORE, EQLI, ICCB, KNGX, LLHE, LLYH, MSFH, MSHE, QQCI
        TSX new-company listings identify these securities as funds or ETFs:
        https://www.tsx.com/en/news/new-company-listings?month=8&year=2024
  The remaining reviewed symbols were cross-checked in TSX new-ETF listing
  reports and official issuer product records. Their recorded names explicitly
  identify ETF units, including the BMO, CI, Desjardins, Evolve, Fidelity,
  Franklin, Global X, Invesco, iShares, LongPoint, Mackenzie, and other fund
  families represented below:
        https://www.tsx.com/en/resource/3253
        https://www.tsx.com/en/resource/3417
        https://www.tsx.com/en/resource/3454
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
        THROW 51900, 'Expected table dbo.Symbols does not exist.', 1;

    IF COL_LENGTH(N'dbo.Symbols', N'Symbol') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'SecurityType') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsActive') IS NULL
       OR COL_LENGTH(N'dbo.Symbols', N'IsLeveragedOrInverseEtp') IS NULL
        THROW 51901, 'dbo.Symbols does not have the expected classification columns.', 1;

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
        THROW 51902, 'A checksum full backup completed within the last 24 hours is required.', 1;

    DECLARE @ReviewedFunds TABLE
    (
        Symbol nvarchar(20) NOT NULL PRIMARY KEY,
        ExpectedIsActive bit NOT NULL
    );

    INSERT @ReviewedFunds (Symbol, ExpectedIsActive)
    VALUES
        (N'ABXU',  0),
        (N'AGG',   0),
        (N'CBL',   0),
        (N'CNQU',  0),
        (N'COMU',  0),
        (N'CORE',  0),
        (N'CUIG',  0),
        (N'DCBC',  0),
        (N'DRFG',  0),
        (N'EHE',   0),
        (N'EQLI',  0),
        (N'ESGA',  0),
        (N'FAUS',  0),
        (N'FTHI',  0),
        (N'HUM',   0),
        (N'HUN',   0),
        (N'ICCB',  0),
        (N'JEPI',  1),
        (N'KNGX',  0),
        (N'LLHE',  1),
        (N'LLYH',  0),
        (N'MNXT',  0),
        (N'MSFH',  0),
        (N'MSHE',  1),
        (N'MSTE',  1),
        (N'NBCU',  0),
        (N'PREF',  0),
        (N'QQCI',  0),
        (N'RAAA',  0),
        (N'RBCU',  0),
        (N'TDU',   0),
        (N'USSL',  0),
        (N'XFLI',  0),
        (N'ZCB',   0),
        (N'ZCDB',  0),
        (N'ZCS.L', 0),
        (N'ZDB',   0),
        (N'ZFS.L', 0),
        (N'ZIN',   0),
        (N'ZPS.L', 0),
        (N'ZQB',   0),
        (N'ZSML',  0),
        (N'ZST.L', 0),
        (N'ZUAG',  0),
        (N'ZUS.V', 0),
        (N'ZWEN',  0),
        (N'ZWHC',  0),
        (N'ZWK',   0),
        (N'ZWT',   0);

    IF (SELECT COUNT(*) FROM @ReviewedFunds) <> 49
        THROW 51903, 'The reviewed fund set is incomplete.', 1;

    IF EXISTS
    (
        SELECT reviewed.Symbol
        FROM @ReviewedFunds AS reviewed
        LEFT JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = reviewed.Symbol
        WHERE actual.Symbol IS NULL
    )
        THROW 51904, 'One or more reviewed funds are missing; no changes were applied.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM @ReviewedFunds AS reviewed
        INNER JOIN dbo.Symbols AS actual WITH (UPDLOCK, HOLDLOCK)
            ON actual.Symbol = reviewed.Symbol
        WHERE actual.IsActive <> reviewed.ExpectedIsActive
           OR actual.SecurityType NOT IN (N'Stock', N'ETF')
    )
        THROW 51905, 'A reviewed fund changed after the audit; no changes were applied.', 1;

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
           OR actual.IsActive <> reviewed.ExpectedIsActive
    )
        THROW 51906, 'Fund-classification postcondition failed; transaction will be rolled back.', 1;

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
