CREATE TABLE [dbo].[IntradayCollectionReceipt]
(
    [ReceiptId]              UNIQUEIDENTIFIER NOT NULL,
    [CollectionSlotId]       UNIQUEIDENTIFIER NOT NULL,
    [CycleId]                UNIQUEIDENTIFIER NOT NULL,
    [SessionId]              UNIQUEIDENTIFIER NOT NULL,
    [Symbol]                 NVARCHAR(20)     NOT NULL,
    [IntervalMinutes]        SMALLINT         NOT NULL,
    [RequestStartedUtc]      DATETIME2        NOT NULL,
    [ReceivedUtc]            DATETIME2        NOT NULL,
    [SettledUtc]             DATETIME2        NOT NULL,
    [Disposition]            NVARCHAR(32)     NOT NULL,
    [DispositionCode]        NVARCHAR(64)     NOT NULL,
    [OperationallyUsable]    BIT              NOT NULL,
    [PollObservationId]      UNIQUEIDENTIFIER NOT NULL,
    [EvidenceBarId]          UNIQUEIDENTIFIER NULL,
    [NormalizedResponseJson] NVARCHAR(MAX)    NOT NULL,
    [ReceiptSha256]          BINARY(32)       NOT NULL,
    [ProviderAttemptCount]   INT              NULL,
    [ProviderRequestCount]   INT              NULL,
    [ProviderFetchStartedUtc] DATETIME2       NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_IntradayCollectionReceipt_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_IntradayCollectionReceipt] PRIMARY KEY CLUSTERED ([ReceiptId]),
    CONSTRAINT [FK_IntradayCollectionReceipt_Slot] FOREIGN KEY
        ([CollectionSlotId], [CycleId], [SessionId], [Symbol], [IntervalMinutes])
        REFERENCES [dbo].[IntradayCollectionSlot]
        ([CollectionSlotId], [CycleId], [SessionId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [FK_IntradayCollectionReceipt_Poll] FOREIGN KEY ([PollObservationId], [Symbol], [IntervalMinutes])
        REFERENCES [dbo].[IntradayPollObservation] ([ObservationId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [FK_IntradayCollectionReceipt_Bar] FOREIGN KEY ([EvidenceBarId])
        REFERENCES [dbo].[IntradayEvidenceBar] ([EvidenceBarId]),
    CONSTRAINT [UQ_IntradayCollectionReceipt_Idempotency] UNIQUE ([CollectionSlotId], [ReceiptSha256]),
    CONSTRAINT [CK_IntradayCollectionReceipt_Time] CHECK
        ([ReceivedUtc] >= [RequestStartedUtc] AND [SettledUtc] >= [ReceivedUtc]),
    CONSTRAINT [CK_IntradayCollectionReceipt_Response] CHECK
        (ISJSON([NormalizedResponseJson]) = 1 AND LEN(LTRIM(RTRIM([DispositionCode]))) > 0),
    CONSTRAINT [CK_IntradayCollectionReceipt_Transport] CHECK
    (
        (([ProviderAttemptCount] IS NULL AND [ProviderRequestCount] IS NULL)
         OR ([ProviderAttemptCount] IS NOT NULL AND [ProviderRequestCount] IS NOT NULL
             AND [ProviderAttemptCount] >= 0 AND [ProviderRequestCount] >= 0))
        AND ([ProviderFetchStartedUtc] IS NULL
             OR ([ProviderFetchStartedUtc] >= [RequestStartedUtc] AND [ProviderFetchStartedUtc] <= [ReceivedUtc]))
    ),
    CONSTRAINT [CK_IntradayCollectionReceipt_Disposition] CHECK
    (
        [Disposition] IN (N'OperationalOnTime', N'IdenticalDuplicate', N'LateResearchOnly', N'NoCompletedBar',
            N'StaleNoNewBar', N'FormingBarIgnored', N'StructurallyInvalid', N'ConflictingDuplicate',
            N'CycleDeadlineExceeded', N'CollectionFailed')
        AND (([OperationallyUsable] = 1 AND [Disposition] IN (N'OperationalOnTime', N'IdenticalDuplicate')
              AND [EvidenceBarId] IS NOT NULL)
             OR ([OperationallyUsable] = 0 AND [Disposition] NOT IN (N'OperationalOnTime', N'IdenticalDuplicate')))
    )
);
GO
