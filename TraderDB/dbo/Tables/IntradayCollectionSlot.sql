CREATE TABLE [dbo].[IntradayCollectionSlot]
(
    [CollectionSlotId]       UNIQUEIDENTIFIER NOT NULL,
    [CycleId]                UNIQUEIDENTIFIER NOT NULL,
    [SessionId]              UNIQUEIDENTIFIER NOT NULL,
    [SessionSymbolId]        UNIQUEIDENTIFIER NOT NULL,
    [Symbol]                 NVARCHAR(20)     NOT NULL,
    [IntervalMinutes]        SMALLINT         NOT NULL,
    [ExpectedBarStartUtc]    DATETIME2        NOT NULL,
    [ExpectedBarEndUtc]      DATETIME2        NOT NULL,
    [ScheduledStartUtc]      DATETIME2        NOT NULL,
    [DeadlineUtc]            DATETIME2        NOT NULL,
    [IsXiuBenchmark]         BIT              NOT NULL,
    [PriorityClass]          NVARCHAR(32)     NOT NULL,
    [PriorityOrdinal]        INT              NOT NULL,
    [RequiredByJson]         NVARCHAR(MAX)    NOT NULL,
    [RequestAttemptCount]    INT              NOT NULL,
    [RequestStartedUtc]      DATETIME2        NULL,
    [ReceivedUtc]            DATETIME2        NULL,
    [PollCycleId]            UNIQUEIDENTIFIER NULL,
    [PollObservationId]      UNIQUEIDENTIFIER NULL,
    [EvidenceBarId]          UNIQUEIDENTIFIER NULL,
    [EvidenceBarEventUtc]    DATETIME2        NULL,
    [Disposition]            NVARCHAR(32)     NOT NULL,
    [DispositionCode]        NVARCHAR(64)     NULL,
    [OperationallyUsable]    BIT              NOT NULL,
    [MissedOperationalDeadline] BIT           NOT NULL,
    [SettledUtc]             DATETIME2        NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_IntradayCollectionSlot_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_IntradayCollectionSlot_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_IntradayCollectionSlot] PRIMARY KEY CLUSTERED ([CollectionSlotId]),
    CONSTRAINT [FK_IntradayCollectionSlot_CycleSchedule] FOREIGN KEY
        ([CycleId], [SessionId], [IntervalMinutes], [ExpectedBarStartUtc], [ExpectedBarEndUtc], [ScheduledStartUtc], [DeadlineUtc])
        REFERENCES [dbo].[IntradayCollectionCycle]
        ([CycleId], [SessionId], [IntervalMinutes], [BarStartUtc], [BarEndUtc], [ScheduledStartUtc], [DeadlineUtc]),
    CONSTRAINT [FK_IntradayCollectionSlot_SessionSymbol] FOREIGN KEY ([SessionSymbolId], [SessionId], [Symbol])
        REFERENCES [dbo].[DelphiLiveSessionSymbol] ([SessionSymbolId], [SessionId], [Symbol]),
    -- The legacy source ledgers expose ID and natural-key uniqueness separately.
    -- Repository writes must resolve both links in one source query before insert.
    CONSTRAINT [FK_IntradayCollectionSlot_PollNaturalKey] FOREIGN KEY ([PollCycleId], [Symbol], [IntervalMinutes])
        REFERENCES [dbo].[IntradayPollObservation] ([PollCycleId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [FK_IntradayCollectionSlot_PollIdentity] FOREIGN KEY ([PollObservationId], [Symbol], [IntervalMinutes])
        REFERENCES [dbo].[IntradayPollObservation] ([ObservationId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [FK_IntradayCollectionSlot_EvidenceBar] FOREIGN KEY ([EvidenceBarId])
        REFERENCES [dbo].[IntradayEvidenceBar] ([EvidenceBarId]),
    CONSTRAINT [FK_IntradayCollectionSlot_EvidenceNaturalKey] FOREIGN KEY ([Symbol], [IntervalMinutes], [EvidenceBarEventUtc])
        REFERENCES [dbo].[IntradayEvidenceBar] ([Symbol], [IntervalMinutes], [EventUtc]),
    CONSTRAINT [UQ_IntradayCollectionSlot_CycleSymbol] UNIQUE ([CycleId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [UQ_IntradayCollectionSlot_ConflictIdentity] UNIQUE
        ([CollectionSlotId], [CycleId], [SessionId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [CK_IntradayCollectionSlot_ExpectedSchedule] CHECK
    (
        [ExpectedBarEndUtc] = DATEADD(MINUTE, [IntervalMinutes], [ExpectedBarStartUtc])
        AND [ScheduledStartUtc] = DATEADD(MINUTE, 2, [ExpectedBarEndUtc])
        AND [DeadlineUtc] = DATEADD(MINUTE, [IntervalMinutes], [ScheduledStartUtc])
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_Priority] CHECK
    (
        [PriorityClass] IN
        (
            N'PendingProtectiveSell',
            N'HeldSymbol',
            N'XiuBenchmark',
            N'ActiveCandidate',
            N'QuietOrDismissedCandidate'
        )
        AND [PriorityOrdinal] >= 0
        AND ISJSON([RequiredByJson]) = 1
        AND ([IsXiuBenchmark] = 0 OR ([Symbol] = N'XIU' AND [PriorityClass] = N'XiuBenchmark'))
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_Attempt] CHECK
    (
        [RequestAttemptCount] >= 0
        AND ([RequestAttemptCount] = 0 OR [RequestStartedUtc] IS NOT NULL)
        AND ([RequestStartedUtc] IS NULL OR [RequestStartedUtc] >= [ScheduledStartUtc])
        AND ([ReceivedUtc] IS NULL OR ([RequestStartedUtc] IS NOT NULL AND [ReceivedUtc] >= [RequestStartedUtc]))
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_PollLink] CHECK
    (
        ([PollCycleId] IS NULL AND [PollObservationId] IS NULL)
        OR ([PollCycleId] IS NOT NULL AND [PollCycleId] = [CycleId] AND [PollObservationId] IS NOT NULL)
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_EvidenceLink] CHECK
    (
        ([EvidenceBarId] IS NULL AND [EvidenceBarEventUtc] IS NULL)
        OR
        (
            [EvidenceBarId] IS NOT NULL
            AND [EvidenceBarEventUtc] IS NOT NULL
            AND [EvidenceBarEventUtc] = [ExpectedBarStartUtc]
        )
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_Disposition] CHECK
    (
        [Disposition] IN
        (
            N'Pending',
            N'OperationalOnTime',
            N'IdenticalDuplicate',
            N'NoCompletedBar',
            N'StaleNoNewBar',
            N'FormingBarIgnored',
            N'StructurallyInvalid',
            N'ConflictingDuplicate',
            N'CycleDeadlineExceeded',
            N'LateResearchOnly',
            N'CollectionFailed',
            N'PersistFailed',
            N'CancelledAtDeadline'
        )
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_Settlement] CHECK
    (
        ([Disposition] = N'Pending' AND [SettledUtc] IS NULL)
        OR
        (
            [Disposition] <> N'Pending'
            AND [SettledUtc] IS NOT NULL
            AND ([ReceivedUtc] IS NULL OR [SettledUtc] >= [ReceivedUtc])
        )
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_OperationalUse] CHECK
    (
        (
            [Disposition] IN (N'OperationalOnTime', N'IdenticalDuplicate')
            AND [OperationallyUsable] = 1
            AND [MissedOperationalDeadline] = 0
            AND [PollObservationId] IS NOT NULL
            AND [EvidenceBarId] IS NOT NULL
            AND [ReceivedUtc] IS NOT NULL
            AND [ReceivedUtc] > [ExpectedBarEndUtc]
            AND [ReceivedUtc] < [DeadlineUtc]
            AND [SettledUtc] < [DeadlineUtc]
        )
        OR
        (
            [Disposition] = N'Pending'
            AND [OperationallyUsable] = 0
            AND [MissedOperationalDeadline] = 0
        )
        OR
        (
            [Disposition] NOT IN (N'Pending', N'OperationalOnTime', N'IdenticalDuplicate')
            AND [OperationallyUsable] = 0
            AND [MissedOperationalDeadline] = 1
        )
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_LateResearch] CHECK
    (
        [Disposition] <> N'LateResearchOnly'
        OR
        (
            [MissedOperationalDeadline] = 1
            AND [PollObservationId] IS NOT NULL
            AND [EvidenceBarId] IS NOT NULL
            AND ([ReceivedUtc] >= [DeadlineUtc] OR [SettledUtc] >= [DeadlineUtc]
                 OR [DispositionCode] IN (N'LeaseLost', N'CycleAlreadySettled'))
        )
    ),
    CONSTRAINT [CK_IntradayCollectionSlot_MissedDeadline] CHECK
        ([Disposition] NOT IN (N'CycleDeadlineExceeded', N'LateResearchOnly', N'CancelledAtDeadline') OR [MissedOperationalDeadline] = 1)
);
GO

CREATE INDEX [IX_IntradayCollectionSlot_CyclePriority]
    ON [dbo].[IntradayCollectionSlot] ([CycleId], [PriorityOrdinal], [Symbol])
    INCLUDE ([PriorityClass], [DeadlineUtc], [Disposition], [OperationallyUsable]);
GO

CREATE INDEX [IX_IntradayCollectionSlot_SymbolEndpoint]
    ON [dbo].[IntradayCollectionSlot] ([Symbol], [ExpectedBarEndUtc] DESC)
    INCLUDE ([CycleId], [Disposition], [OperationallyUsable], [EvidenceBarId]);
GO
