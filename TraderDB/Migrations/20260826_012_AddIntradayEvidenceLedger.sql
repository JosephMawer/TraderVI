USE [TraderDB];
GO

SET XACT_ABORT ON;

/*
    Intraday evidence and poll-audit ledger (ADR-0030).

    Expected precondition:
      - dbo.Symbols exists.
      - dbo.IntradayPollObservation and dbo.IntradayEvidenceBar are either both
        absent (first application) or both already present (verification rerun).

    Data effect:
      - Creates two empty additive tables, their keys/checks/defaults, and one
        supporting observation index. Existing rows and objects are unchanged.

    Recovery:
      - No rollback objects are included because dropping evidence tables is
        destructive. Before application, create and verify a fresh full backup.
        If the transaction fails, XACT_ABORT rolls back the complete change.
        After a committed application, recovery is from that reviewed backup or
        a separately authorized corrective migration.

    Do not deploy a DACPAC. Review and execute this script manually only after
    explicit authorization.
*/

IF OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
    THROW 51020, 'Prerequisite dbo.Symbols does not exist. Intraday evidence migration was not started.', 1;

DECLARE @ObservationExists BIT = CASE WHEN OBJECT_ID(N'dbo.IntradayPollObservation', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @BarExists BIT = CASE WHEN OBJECT_ID(N'dbo.IntradayEvidenceBar', N'U') IS NULL THEN 0 ELSE 1 END;

IF @ObservationExists <> @BarExists
    THROW 51021, 'Partial intraday evidence schema exists. Migration refused without changing the database.', 1;

BEGIN TRANSACTION;

IF @ObservationExists = 0
BEGIN
    CREATE TABLE [dbo].[IntradayPollObservation]
    (
        [ObservationId]         UNIQUEIDENTIFIER NOT NULL,
        [PollCycleId]           UNIQUEIDENTIFIER NOT NULL,
        [Purpose]               NVARCHAR(32)     NOT NULL,
        [Symbol]                NVARCHAR(20)     NOT NULL,
        [IntervalMinutes]       SMALLINT         NOT NULL,
        [EvidenceSchemaVersion] INT              NOT NULL,
        [Provider]              NVARCHAR(32)     NOT NULL,
        [SourceContractVersion] NVARCHAR(64)     NOT NULL,
        [CollectorVersion]      NVARCHAR(64)     NOT NULL,
        [PolicyVersion]         NVARCHAR(64)     NULL,
        [CodeCommit]            NVARCHAR(128)    NOT NULL,
        [WorkingTreeState]      NVARCHAR(16)     NOT NULL,
        [RequestedStartUtc]     DATETIME2        NOT NULL,
        [RequestedEndUtc]       DATETIME2        NOT NULL,
        [FetchStartedUtc]       DATETIME2        NOT NULL,
        [ReceivedUtc]           DATETIME2        NULL,
        [AttemptCount]          INT              NOT NULL,
        [RequestCount]          INT              NOT NULL,
        [ReturnedBarCount]      INT              NOT NULL,
        [CompletedBarCount]     INT              NOT NULL,
        [PersistedNewBarCount]  INT              NOT NULL,
        [LatestReturnedEventUtc]  DATETIME2      NULL,
        [LatestCompletedEventUtc] DATETIME2      NULL,
        [AuditState]            NVARCHAR(16)     NOT NULL,
        [AuditCode]             NVARCHAR(64)     NULL,
        [CreatedUtc]            DATETIME2        NOT NULL CONSTRAINT [DF_IntradayPollObservation_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT [PK_IntradayPollObservation] PRIMARY KEY CLUSTERED ([ObservationId]),
        CONSTRAINT [FK_IntradayPollObservation_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols] ([Symbol]),
        CONSTRAINT [UQ_IntradayPollObservation_CycleSymbolInterval] UNIQUE ([PollCycleId], [Symbol], [IntervalMinutes]),
        CONSTRAINT [UQ_IntradayPollObservation_Identity] UNIQUE ([ObservationId], [Symbol], [IntervalMinutes]),
        CONSTRAINT [CK_IntradayPollObservation_Purpose] CHECK ([Purpose] IN ('PaperMonitor', 'Backfill', 'Probe')),
        CONSTRAINT [CK_IntradayPollObservation_Interval] CHECK ([IntervalMinutes] IN (5, 15)),
        CONSTRAINT [CK_IntradayPollObservation_SchemaVersion] CHECK ([EvidenceSchemaVersion] = 1),
        CONSTRAINT [CK_IntradayPollObservation_WorkingTreeState] CHECK ([WorkingTreeState] IN ('Clean', 'Dirty', 'Unknown')),
        CONSTRAINT [CK_IntradayPollObservation_AuditState] CHECK ([AuditState] IN ('Valid', 'Degraded', 'Invalid', 'Failed')),
        CONSTRAINT [CK_IntradayPollObservation_RequestWindow] CHECK ([RequestedStartUtc] <= [RequestedEndUtc]),
        CONSTRAINT [CK_IntradayPollObservation_Receipt] CHECK ([ReceivedUtc] IS NULL OR [FetchStartedUtc] <= [ReceivedUtc]),
        CONSTRAINT [CK_IntradayPollObservation_Counts] CHECK
        (
            [AttemptCount] >= 0
            AND [RequestCount] >= 0
            AND [ReturnedBarCount] >= 0
            AND [CompletedBarCount] >= 0
            AND [CompletedBarCount] <= [ReturnedBarCount]
            AND [PersistedNewBarCount] >= 0
            AND [PersistedNewBarCount] <= [CompletedBarCount]
        ),
        CONSTRAINT [CK_IntradayPollObservation_LatestReturned] CHECK
        (
            ([ReturnedBarCount] = 0 AND [LatestReturnedEventUtc] IS NULL)
            OR ([ReturnedBarCount] > 0 AND [LatestReturnedEventUtc] IS NOT NULL)
        ),
        CONSTRAINT [CK_IntradayPollObservation_LatestCompleted] CHECK
        (
            ([CompletedBarCount] = 0 AND [LatestCompletedEventUtc] IS NULL)
            OR ([CompletedBarCount] > 0 AND [LatestCompletedEventUtc] IS NOT NULL)
        ),
        CONSTRAINT [CK_IntradayPollObservation_EventOrder] CHECK
        (
            [LatestCompletedEventUtc] IS NULL
            OR [LatestReturnedEventUtc] IS NULL
            OR [LatestCompletedEventUtc] <= [LatestReturnedEventUtc]
        ),
        CONSTRAINT [CK_IntradayPollObservation_ReceivedAudit] CHECK
        (
            [AuditState] = 'Failed'
            OR [ReceivedUtc] IS NOT NULL
        )
    );

    CREATE INDEX [IX_IntradayPollObservation_SymbolIntervalEnd]
        ON [dbo].[IntradayPollObservation] ([Symbol], [IntervalMinutes], [RequestedEndUtc] DESC)
        INCLUDE ([ReceivedUtc], [AuditState], [LatestCompletedEventUtc]);

    CREATE TABLE [dbo].[IntradayEvidenceBar]
    (
        [EvidenceBarId]      UNIQUEIDENTIFIER NOT NULL,
        [FirstObservationId] UNIQUEIDENTIFIER NOT NULL,
        [Symbol]             NVARCHAR(20)     NOT NULL,
        [IntervalMinutes]    SMALLINT         NOT NULL,
        [EventUtc]           DATETIME2        NOT NULL,
        [Open]               DECIMAL(19,6)    NOT NULL,
        [High]               DECIMAL(19,6)    NOT NULL,
        [Low]                DECIMAL(19,6)    NOT NULL,
        [Close]              DECIMAL(19,6)    NOT NULL,
        [Volume]             BIGINT           NOT NULL,
        [CreatedUtc]         DATETIME2        NOT NULL CONSTRAINT [DF_IntradayEvidenceBar_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT [PK_IntradayEvidenceBar] PRIMARY KEY CLUSTERED ([EvidenceBarId]),
        CONSTRAINT [FK_IntradayEvidenceBar_FirstObservation] FOREIGN KEY ([FirstObservationId], [Symbol], [IntervalMinutes])
            REFERENCES [dbo].[IntradayPollObservation] ([ObservationId], [Symbol], [IntervalMinutes]),
        CONSTRAINT [FK_IntradayEvidenceBar_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols] ([Symbol]),
        CONSTRAINT [UQ_IntradayEvidenceBar_SymbolIntervalEvent] UNIQUE ([Symbol], [IntervalMinutes], [EventUtc]),
        CONSTRAINT [CK_IntradayEvidenceBar_Interval] CHECK ([IntervalMinutes] IN (5, 15)),
        CONSTRAINT [CK_IntradayEvidenceBar_EventAlignment] CHECK
        (
            DATEPART(SECOND, [EventUtc]) = 0
            AND DATEPART(NANOSECOND, [EventUtc]) = 0
            AND DATEPART(MINUTE, [EventUtc]) % [IntervalMinutes] = 0
        ),
        CONSTRAINT [CK_IntradayEvidenceBar_Ohlc] CHECK
        (
            [Open] > 0
            AND [High] > 0
            AND [Low] > 0
            AND [Close] > 0
            AND [Low] <= [Open]
            AND [Low] <= [Close]
            AND [High] >= [Open]
            AND [High] >= [Close]
            AND [Low] <= [High]
        ),
        CONSTRAINT [CK_IntradayEvidenceBar_Volume] CHECK ([Volume] >= 0)
    );
END;

IF OBJECT_ID(N'dbo.IntradayPollObservation', N'U') IS NULL
   OR OBJECT_ID(N'dbo.IntradayEvidenceBar', N'U') IS NULL
   OR COL_LENGTH(N'dbo.IntradayPollObservation', N'EvidenceSchemaVersion') IS NULL
   OR COL_LENGTH(N'dbo.IntradayPollObservation', N'LatestCompletedEventUtc') IS NULL
   OR COL_LENGTH(N'dbo.IntradayEvidenceBar', N'FirstObservationId') IS NULL
   OR COL_LENGTH(N'dbo.IntradayEvidenceBar', N'EventUtc') IS NULL
   OR ISNULL(INDEXPROPERTY(OBJECT_ID(N'dbo.IntradayPollObservation'), N'UQ_IntradayPollObservation_CycleSymbolInterval', 'IsUnique'), 0) <> 1
   OR ISNULL(INDEXPROPERTY(OBJECT_ID(N'dbo.IntradayEvidenceBar'), N'UQ_IntradayEvidenceBar_SymbolIntervalEvent', 'IsUnique'), 0) <> 1
   OR OBJECT_ID(N'dbo.FK_IntradayEvidenceBar_FirstObservation', N'F') IS NULL
    THROW 51022, 'Intraday evidence schema verification failed. Transaction will be rolled back.', 1;

COMMIT TRANSACTION;

SELECT
    [SchemaName] = s.[name],
    [ObjectName] = o.[name],
    [ObjectType] = o.[type_desc]
FROM sys.objects o
JOIN sys.schemas s ON s.[schema_id] = o.[schema_id]
WHERE s.[name] = N'dbo'
  AND o.[name] IN (N'IntradayPollObservation', N'IntradayEvidenceBar')
UNION ALL
SELECT
    [SchemaName] = s.[name],
    [ObjectName] = i.[name],
    [ObjectType] = N'INDEX'
FROM sys.indexes i
JOIN sys.objects o ON o.[object_id] = i.[object_id]
JOIN sys.schemas s ON s.[schema_id] = o.[schema_id]
WHERE s.[name] = N'dbo'
  AND i.[name] = N'IX_IntradayPollObservation_SymbolIntervalEnd'
ORDER BY [ObjectName];
