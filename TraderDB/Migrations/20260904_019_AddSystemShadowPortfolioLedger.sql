:ON ERROR EXIT

USE [TraderDB];
GO

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

/*
    Add the Shadow V1 alternative-portfolio ledger under ADR-0051.

    Expected preconditions:
      - Migrations through 018 are applied.
      - The calibration and symbol ledgers exist.
      - All eight Shadow tables are absent. A partial earlier attempt must be
        reviewed instead of silently repaired.

    Data effects:
      - Schema only. No generation is seeded and Shadow remains off by default.
      - Adds durable portfolio, session, frozen-candidate, position, causal
        order, event, and capital-snapshot records.

    Recovery:
      - The DDL is one SERIALIZABLE transaction with XACT_ABORT.
      - Restore the fresh verified pre-migration backup if an authorized
        rollback is required after commit. Do not edit this migration.

    Review and execute from the repository root with SQLCMD only after a fresh
    verified backup and explicit authorization. Do not deploy a DACPAC.
*/

IF OBJECT_ID(N'dbo.CalibrationRun', N'U') IS NULL
    OR OBJECT_ID(N'dbo.CalibrationCandidate', N'U') IS NULL
    OR OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
    THROW 51120, 'Required calibration or symbol ledger is missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.tables
    WHERE [object_id] IN
    (
        OBJECT_ID(N'dbo.ShadowPortfolioGeneration'),
        OBJECT_ID(N'dbo.ShadowPortfolio'),
        OBJECT_ID(N'dbo.ShadowPortfolioSession'),
        OBJECT_ID(N'dbo.ShadowPortfolioCandidate'),
        OBJECT_ID(N'dbo.ShadowPosition'),
        OBJECT_ID(N'dbo.ShadowOrder'),
        OBJECT_ID(N'dbo.ShadowPortfolioEvent'),
        OBJECT_ID(N'dbo.ShadowCapitalEvent')
    )
)
    THROW 51121, 'A Shadow ledger table already exists; review the partial or prior installation.', 1;

BEGIN TRANSACTION;
GO

:r TraderDB\dbo\Tables\ShadowPortfolioGeneration.sql
:r TraderDB\dbo\Tables\ShadowPortfolio.sql
:r TraderDB\dbo\Tables\ShadowPortfolioSession.sql
:r TraderDB\dbo\Tables\ShadowPortfolioCandidate.sql
:r TraderDB\dbo\Tables\ShadowPosition.sql
:r TraderDB\dbo\Tables\ShadowOrder.sql
:r TraderDB\dbo\Tables\ShadowPortfolioEvent.sql
:r TraderDB\dbo\Tables\ShadowCapitalEvent.sql

IF
(
    SELECT COUNT(*)
    FROM sys.tables
    WHERE [object_id] IN
    (
        OBJECT_ID(N'dbo.ShadowPortfolioGeneration'),
        OBJECT_ID(N'dbo.ShadowPortfolio'),
        OBJECT_ID(N'dbo.ShadowPortfolioSession'),
        OBJECT_ID(N'dbo.ShadowPortfolioCandidate'),
        OBJECT_ID(N'dbo.ShadowPosition'),
        OBJECT_ID(N'dbo.ShadowOrder'),
        OBJECT_ID(N'dbo.ShadowPortfolioEvent'),
        OBJECT_ID(N'dbo.ShadowCapitalEvent')
    )
) <> 8
    THROW 51122, 'The complete Shadow ledger was not created.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.ShadowPortfolio'),
        OBJECT_ID(N'dbo.ShadowPortfolioSession'),
        OBJECT_ID(N'dbo.ShadowPortfolioCandidate'),
        OBJECT_ID(N'dbo.ShadowPosition'),
        OBJECT_ID(N'dbo.ShadowOrder'),
        OBJECT_ID(N'dbo.ShadowPortfolioEvent'),
        OBJECT_ID(N'dbo.ShadowCapitalEvent')
    )
      AND (is_disabled = 1 OR is_not_trusted = 1)
)
    THROW 51123, 'A Shadow foreign key is disabled or untrusted.', 1;

COMMIT TRANSACTION;
GO

SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE [name] LIKE N'Shadow%') AS [ShadowTableCount],
    (SELECT COUNT_BIG(*) FROM dbo.ShadowPortfolioGeneration) AS [GenerationRows],
    (SELECT COUNT_BIG(*) FROM dbo.ShadowPortfolio) AS [PortfolioRows],
    (SELECT COUNT_BIG(*) FROM dbo.ShadowOrder) AS [OrderRows];
GO

