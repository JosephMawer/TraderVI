-- One-time, idempotent deployment for the Market Climax table.
-- Run against the local TraderDB database before starting Hermes.

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'TraderDB'
    THROW 50000, 'This script must be run against the TraderDB database.', 1;

IF OBJECT_ID(N'[dbo].[MarketClimax]', N'U') IS NOT NULL
BEGIN
    PRINT N'[dbo].[MarketClimax] already exists; no changes were made.';
    RETURN;
END;

CREATE TABLE [dbo].[MarketClimax]
(
    [Date]          DATE          NOT NULL,
    [UpBreakouts]   INT           NOT NULL,
    [DownBreakouts] INT           NOT NULL,
    [Clx]           INT           NOT NULL,
    [FreshUp]       INT           NOT NULL,
    [FreshDown]     INT           NOT NULL,
    [Covered]       INT           NOT NULL,
    [BasketSize]    INT           NOT NULL,
    [XiuClose]      REAL          NULL,
    [CreatedAt]     DATETIME2 (7) NOT NULL
        CONSTRAINT [DF_MarketClimax_CreatedAt] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_MarketClimax]
        PRIMARY KEY CLUSTERED ([Date] ASC)
);

PRINT N'Created [dbo].[MarketClimax].';
