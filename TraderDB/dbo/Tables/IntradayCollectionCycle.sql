CREATE TABLE [dbo].[IntradayCollectionCycle]
(
    [CycleId]                       UNIQUEIDENTIFIER NOT NULL,
    [SessionId]                     UNIQUEIDENTIFIER NOT NULL,
    [ContinuityEpochId]             UNIQUEIDENTIFIER NOT NULL,
    [LeaseId]                       UNIQUEIDENTIFIER NOT NULL,
    [LeaseFencingToken]             BIGINT           NOT NULL,
    [CollectionPurpose]             NVARCHAR(32)     NOT NULL,
    [Provider]                      NVARCHAR(32)     NOT NULL,
    [CollectorVersion]              NVARCHAR(64)     NOT NULL,
    [SourceContractVersion]         INT              NOT NULL,
    [IntervalMinutes]               SMALLINT         NOT NULL,
    [BarStartUtc]                   DATETIME2        NOT NULL,
    [BarEndUtc]                     DATETIME2        NOT NULL,
    [ScheduledStartUtc]             DATETIME2        NOT NULL,
    [DeadlineUtc]                   DATETIME2        NOT NULL,
    [CycleStatus]                   NVARCHAR(32)     NOT NULL,
    [ExpectedSlotCount]             INT              NOT NULL,
    [SettledSlotCount]              INT              NOT NULL,
    [StartedUtc]                    DATETIME2        NULL,
    [CompletedUtc]                  DATETIME2        NULL,
    [CompletionCode]                NVARCHAR(64)     NULL,
    [CreatedUtc]                    DATETIME2        NOT NULL CONSTRAINT [DF_IntradayCollectionCycle_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]                    DATETIME2        NOT NULL CONSTRAINT [DF_IntradayCollectionCycle_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),
    [ActiveCycleScope] AS
        (CASE WHEN [CycleStatus] = N'Collecting' THEN CONVERT(TINYINT, (1)) ELSE NULL END) PERSISTED,

    CONSTRAINT [PK_IntradayCollectionCycle] PRIMARY KEY CLUSTERED ([CycleId]),
    CONSTRAINT [FK_IntradayCollectionCycle_Session] FOREIGN KEY ([SessionId])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId]),
    CONSTRAINT [FK_IntradayCollectionCycle_Continuity] FOREIGN KEY
        ([ContinuityEpochId], [SessionId], [LeaseId], [LeaseFencingToken])
        REFERENCES [dbo].[DelphiLiveContinuityEpoch]
        ([ContinuityEpochId], [SessionId], [LeaseId], [LeaseFencingToken]),
    CONSTRAINT [UQ_IntradayCollectionCycle_Endpoint] UNIQUE
        ([CollectorVersion], [SourceContractVersion], [IntervalMinutes], [BarEndUtc]),
    CONSTRAINT [UQ_IntradayCollectionCycle_SlotIdentity] UNIQUE
        ([CycleId], [SessionId], [IntervalMinutes], [BarStartUtc], [BarEndUtc], [ScheduledStartUtc], [DeadlineUtc]),
    CONSTRAINT [CK_IntradayCollectionCycle_Identity] CHECK
    (
        [CollectionPurpose] = N'DelphiLiveShared'
        AND LEN(LTRIM(RTRIM([Provider]))) > 0
        AND [CollectorVersion] = N'IntradayEvidenceCollectorV3'
        AND [SourceContractVersion] = 1
        AND [IntervalMinutes] = 5
        AND [LeaseFencingToken] > 0
    ),
    CONSTRAINT [CK_IntradayCollectionCycle_Schedule] CHECK
    (
        [BarEndUtc] = DATEADD(MINUTE, [IntervalMinutes], [BarStartUtc])
        AND [ScheduledStartUtc] = DATEADD(MINUTE, 2, [BarEndUtc])
        AND [DeadlineUtc] = DATEADD(MINUTE, [IntervalMinutes], [ScheduledStartUtc])
        AND DATEPART(SECOND, [BarStartUtc]) = 0
        AND DATEPART(NANOSECOND, [BarStartUtc]) = 0
        AND DATEPART(MINUTE, [BarStartUtc]) % [IntervalMinutes] = 0
    ),
    CONSTRAINT [CK_IntradayCollectionCycle_Counts] CHECK
    (
        [ExpectedSlotCount] >= 0
        AND [SettledSlotCount] >= 0
        AND [SettledSlotCount] <= [ExpectedSlotCount]
    ),
    CONSTRAINT [CK_IntradayCollectionCycle_State] CHECK
    (
        (
            [CycleStatus] = N'Planned'
            AND [StartedUtc] IS NULL
            AND [CompletedUtc] IS NULL
            AND [CompletionCode] IS NULL
        )
        OR
        (
            [CycleStatus] = N'Collecting'
            AND [StartedUtc] IS NOT NULL
            AND [StartedUtc] >= [ScheduledStartUtc]
            AND [StartedUtc] < [DeadlineUtc]
            AND [CompletedUtc] IS NULL
            AND [CompletionCode] IS NULL
        )
        OR
        (
            [CycleStatus] = N'Completed'
            AND [StartedUtc] IS NOT NULL
            AND [StartedUtc] >= [ScheduledStartUtc]
            AND [StartedUtc] < [DeadlineUtc]
            AND [CompletedUtc] IS NOT NULL
            AND [CompletedUtc] >= [StartedUtc]
            AND LEN(LTRIM(RTRIM([CompletionCode]))) > 0
        )
        OR
        (
            [CycleStatus] = N'DeadlineExceeded'
            AND [CompletedUtc] IS NOT NULL
            AND
            (
                [StartedUtc] IS NULL
                OR ([StartedUtc] >= [ScheduledStartUtc] AND [StartedUtc] <= [CompletedUtc])
            )
            AND [CompletedUtc] >= [DeadlineUtc]
            AND LEN(LTRIM(RTRIM([CompletionCode]))) > 0
        )
        OR
        (
            [CycleStatus] = N'Cancelled'
            AND [CompletedUtc] IS NOT NULL
            AND
            (
                [StartedUtc] IS NULL
                OR ([StartedUtc] >= [ScheduledStartUtc] AND [StartedUtc] <= [CompletedUtc])
            )
            AND [CompletedUtc] >= [ScheduledStartUtc]
            AND LEN(LTRIM(RTRIM([CompletionCode]))) > 0
        )
    )
);
GO

CREATE UNIQUE INDEX [UX_IntradayCollectionCycle_OneCollecting]
    ON [dbo].[IntradayCollectionCycle] ([ActiveCycleScope])
    WHERE [CycleStatus] = N'Collecting';
GO

CREATE INDEX [IX_IntradayCollectionCycle_SessionEndpoint]
    ON [dbo].[IntradayCollectionCycle] ([SessionId], [BarEndUtc])
    INCLUDE ([ScheduledStartUtc], [DeadlineUtc], [CycleStatus], [ExpectedSlotCount], [SettledSlotCount]);
GO
