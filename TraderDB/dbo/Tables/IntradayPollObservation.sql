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
GO

CREATE INDEX [IX_IntradayPollObservation_SymbolIntervalEnd]
    ON [dbo].[IntradayPollObservation] ([Symbol], [IntervalMinutes], [RequestedEndUtc] DESC)
    INCLUDE ([ReceivedUtc], [AuditState], [LatestCompletedEventUtc]);
GO
