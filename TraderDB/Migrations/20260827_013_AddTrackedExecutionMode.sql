USE [TraderDB];
GO

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

/*
    Explicit Ghost/Real tracked-execution mode (ADR-0039).

    Expected precondition:
      - dbo.ActivePosition and dbo.TradeLog exist.
      - ExecutionMode and AccountLabel are either absent from both tables, or
        present on both tables with dbo.PositionExecutionAudit also present.

    Data effect:
      - Adds ExecutionMode and AccountLabel to both operational tables.
      - Classifies every existing position and trade as Ghost. No historical row
        is inferred to be a real broker fill.
      - Adds an immutable audit table for later operator-confirmed mode changes.

    Recovery:
      - This is additive and no rollback objects are included because dropping
        columns or the audit table would be destructive. Create and verify a
        fresh full backup before manual application. XACT_ABORT rolls back a
        failed transaction; after commit use the verified backup or a separately
        authorized corrective migration.

    Do not deploy a DACPAC. Review and execute this script manually only after
    explicit authorization.
*/

IF OBJECT_ID(N'dbo.ActivePosition', N'U') IS NULL
    OR OBJECT_ID(N'dbo.TradeLog', N'U') IS NULL
    THROW 51030, 'Prerequisite position tables do not exist. Execution-mode migration was not started.', 1;

DECLARE @PositionModeExists BIT = CASE WHEN COL_LENGTH(N'dbo.ActivePosition', N'ExecutionMode') IS NULL THEN 0 ELSE 1 END;
DECLARE @PositionAccountExists BIT = CASE WHEN COL_LENGTH(N'dbo.ActivePosition', N'AccountLabel') IS NULL THEN 0 ELSE 1 END;
DECLARE @TradeModeExists BIT = CASE WHEN COL_LENGTH(N'dbo.TradeLog', N'ExecutionMode') IS NULL THEN 0 ELSE 1 END;
DECLARE @TradeAccountExists BIT = CASE WHEN COL_LENGTH(N'dbo.TradeLog', N'AccountLabel') IS NULL THEN 0 ELSE 1 END;
DECLARE @AuditExists BIT = CASE WHEN OBJECT_ID(N'dbo.PositionExecutionAudit', N'U') IS NULL THEN 0 ELSE 1 END;

IF @PositionModeExists <> @PositionAccountExists
    OR @PositionModeExists <> @TradeModeExists
    OR @PositionModeExists <> @TradeAccountExists
    OR @PositionModeExists <> @AuditExists
    THROW 51031, 'Partial tracked-execution schema exists. Migration refused without changing the database.', 1;

BEGIN TRANSACTION;

IF @PositionModeExists = 0
BEGIN
    -- Defer compilation until the new columns exist. SQL Server otherwise binds
    -- later references in this batch against the pre-migration table metadata.
    EXEC sys.sp_executesql N'
        ALTER TABLE [dbo].[ActivePosition]
            ADD [ExecutionMode] NVARCHAR(8) NULL,
                [AccountLabel] NVARCHAR(64) NULL;

        ALTER TABLE [dbo].[TradeLog]
            ADD [ExecutionMode] NVARCHAR(8) NULL,
                [AccountLabel] NVARCHAR(64) NULL;';

    EXEC sys.sp_executesql N'
        UPDATE [dbo].[ActivePosition]
        SET [ExecutionMode] = N''Ghost''
        WHERE [ExecutionMode] IS NULL;

        UPDATE [dbo].[TradeLog]
        SET [ExecutionMode] = N''Ghost''
        WHERE [ExecutionMode] IS NULL;

        ALTER TABLE [dbo].[ActivePosition] ALTER COLUMN [ExecutionMode] NVARCHAR(8) NOT NULL;
        ALTER TABLE [dbo].[TradeLog] ALTER COLUMN [ExecutionMode] NVARCHAR(8) NOT NULL;

        ALTER TABLE [dbo].[ActivePosition]
            ADD CONSTRAINT [DF_ActivePosition_ExecutionMode] DEFAULT (N''Ghost'') FOR [ExecutionMode],
                CONSTRAINT [CK_ActivePosition_ExecutionMode] CHECK ([ExecutionMode] IN (N''Ghost'', N''Real'')),
                CONSTRAINT [CK_ActivePosition_AccountLabel] CHECK
                (
                    ([ExecutionMode] = N''Ghost'' AND [AccountLabel] IS NULL)
                    OR ([ExecutionMode] = N''Real'' AND LEN(LTRIM(RTRIM([AccountLabel]))) BETWEEN 1 AND 64)
                );

        ALTER TABLE [dbo].[TradeLog]
            ADD CONSTRAINT [DF_TradeLog_ExecutionMode] DEFAULT (N''Ghost'') FOR [ExecutionMode],
                CONSTRAINT [CK_TradeLog_ExecutionMode] CHECK ([ExecutionMode] IN (N''Ghost'', N''Real'')),
                CONSTRAINT [CK_TradeLog_AccountLabel] CHECK
                (
                    ([ExecutionMode] = N''Ghost'' AND [AccountLabel] IS NULL)
                    OR ([ExecutionMode] = N''Real'' AND LEN(LTRIM(RTRIM([AccountLabel]))) BETWEEN 1 AND 64)
                );';

    CREATE TABLE [dbo].[PositionExecutionAudit]
    (
        [AuditId]          UNIQUEIDENTIFIER NOT NULL,
        [PositionId]       UNIQUEIDENTIFIER NOT NULL,
        [FromMode]         NVARCHAR(8)      NOT NULL,
        [ToMode]           NVARCHAR(8)      NOT NULL,
        [AccountLabel]     NVARCHAR(64)     NULL,
        [Reason]           NVARCHAR(256)    NOT NULL,
        [CreatedUtc]       DATETIME2        NOT NULL CONSTRAINT [DF_PositionExecutionAudit_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT [PK_PositionExecutionAudit] PRIMARY KEY CLUSTERED ([AuditId]),
        CONSTRAINT [FK_PositionExecutionAudit_Position] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[ActivePosition] ([PositionId]),
        CONSTRAINT [CK_PositionExecutionAudit_FromMode] CHECK ([FromMode] IN (N'Ghost', N'Real')),
        CONSTRAINT [CK_PositionExecutionAudit_ToMode] CHECK ([ToMode] IN (N'Ghost', N'Real')),
        CONSTRAINT [CK_PositionExecutionAudit_Changed] CHECK ([FromMode] <> [ToMode]),
        CONSTRAINT [CK_PositionExecutionAudit_AccountLabel] CHECK
        (
            ([ToMode] = N'Ghost' AND [AccountLabel] IS NULL)
            OR ([ToMode] = N'Real' AND LEN(LTRIM(RTRIM([AccountLabel]))) BETWEEN 1 AND 64)
        )
    );
END;

IF COL_LENGTH(N'dbo.ActivePosition', N'ExecutionMode') IS NULL
    OR COL_LENGTH(N'dbo.ActivePosition', N'AccountLabel') IS NULL
    OR COL_LENGTH(N'dbo.TradeLog', N'ExecutionMode') IS NULL
    OR COL_LENGTH(N'dbo.TradeLog', N'AccountLabel') IS NULL
    OR OBJECT_ID(N'dbo.PositionExecutionAudit', N'U') IS NULL
    OR OBJECT_ID(N'dbo.CK_ActivePosition_ExecutionMode', N'C') IS NULL
    OR OBJECT_ID(N'dbo.CK_TradeLog_ExecutionMode', N'C') IS NULL
    OR OBJECT_ID(N'dbo.FK_PositionExecutionAudit_Position', N'F') IS NULL
    THROW 51032, 'Tracked-execution schema verification failed. Transaction will be rolled back.', 1;

COMMIT TRANSACTION;
GO

SELECT
    [PositionRows] = COUNT_BIG(*),
    [GhostPositions] = SUM(CASE WHEN [ExecutionMode] = N'Ghost' THEN CONVERT(BIGINT, 1) ELSE CONVERT(BIGINT, 0) END),
    [RealPositions] = SUM(CASE WHEN [ExecutionMode] = N'Real' THEN CONVERT(BIGINT, 1) ELSE CONVERT(BIGINT, 0) END)
FROM [dbo].[ActivePosition];

SELECT
    [TradeRows] = COUNT_BIG(*),
    [GhostTrades] = SUM(CASE WHEN [ExecutionMode] = N'Ghost' THEN CONVERT(BIGINT, 1) ELSE CONVERT(BIGINT, 0) END),
    [RealTrades] = SUM(CASE WHEN [ExecutionMode] = N'Real' THEN CONVERT(BIGINT, 1) ELSE CONVERT(BIGINT, 0) END)
FROM [dbo].[TradeLog];
