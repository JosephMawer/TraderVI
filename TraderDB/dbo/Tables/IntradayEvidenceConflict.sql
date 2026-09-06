CREATE TABLE [dbo].[IntradayEvidenceConflict]
(
    [EvidenceConflictId]         UNIQUEIDENTIFIER NOT NULL,
    [CollectionSlotId]           UNIQUEIDENTIFIER NOT NULL,
    [CycleId]                    UNIQUEIDENTIFIER NOT NULL,
    [SessionId]                  UNIQUEIDENTIFIER NOT NULL,
    [PollObservationId]          UNIQUEIDENTIFIER NOT NULL,
    [ExistingEvidenceBarId]      UNIQUEIDENTIFIER NOT NULL,
    [Symbol]                     NVARCHAR(20)     NOT NULL,
    [IntervalMinutes]            SMALLINT         NOT NULL,
    [ExistingBarEventUtc]        DATETIME2        NOT NULL,
    [IncomingEventUtc]           DATETIME2        NOT NULL,
    [IncomingOpen]               DECIMAL(19,6)    NOT NULL,
    [IncomingHigh]               DECIMAL(19,6)    NOT NULL,
    [IncomingLow]                DECIMAL(19,6)    NOT NULL,
    [IncomingClose]              DECIMAL(19,6)    NOT NULL,
    [IncomingVolume]             BIGINT           NOT NULL,
    [IncomingPayloadSha256]      BINARY(32)       NOT NULL,
    [ProviderPayloadReference]   NVARCHAR(512)    NULL,
    [ReceivedUtc]                DATETIME2        NOT NULL,
    [ConflictCode]               NVARCHAR(64)     NOT NULL,
    [ResolutionDisposition]      NVARCHAR(32)     NOT NULL,
    [ResolutionReason]           NVARCHAR(1024)   NULL,
    [ResolvedUtc]                DATETIME2        NULL,
    [CreatedUtc]                 DATETIME2        NOT NULL CONSTRAINT [DF_IntradayEvidenceConflict_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_IntradayEvidenceConflict] PRIMARY KEY CLUSTERED ([EvidenceConflictId]),
    CONSTRAINT [FK_IntradayEvidenceConflict_Slot] FOREIGN KEY
        ([CollectionSlotId], [CycleId], [SessionId], [Symbol], [IntervalMinutes])
        REFERENCES [dbo].[IntradayCollectionSlot]
        ([CollectionSlotId], [CycleId], [SessionId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [FK_IntradayEvidenceConflict_Poll] FOREIGN KEY ([PollObservationId], [Symbol], [IntervalMinutes])
        REFERENCES [dbo].[IntradayPollObservation] ([ObservationId], [Symbol], [IntervalMinutes]),
    CONSTRAINT [FK_IntradayEvidenceConflict_ExistingBar] FOREIGN KEY ([ExistingEvidenceBarId])
        REFERENCES [dbo].[IntradayEvidenceBar] ([EvidenceBarId]),
    CONSTRAINT [FK_IntradayEvidenceConflict_ExistingNaturalKey] FOREIGN KEY
        ([Symbol], [IntervalMinutes], [ExistingBarEventUtc])
        REFERENCES [dbo].[IntradayEvidenceBar] ([Symbol], [IntervalMinutes], [EventUtc]),
    CONSTRAINT [UQ_IntradayEvidenceConflict_Payload] UNIQUE ([CollectionSlotId], [IncomingPayloadSha256]),
    -- Repository writes must verify the independently keyed legacy observation
    -- and evidence IDs identify these same natural-key rows.
    CONSTRAINT [CK_IntradayEvidenceConflict_Event] CHECK
        ([IncomingEventUtc] = [ExistingBarEventUtc] AND [ReceivedUtc] > [IncomingEventUtc]),
    CONSTRAINT [CK_IntradayEvidenceConflict_Ohlc] CHECK
    (
        [IncomingOpen] > 0
        AND [IncomingHigh] > 0
        AND [IncomingLow] > 0
        AND [IncomingClose] > 0
        AND [IncomingLow] <= [IncomingOpen]
        AND [IncomingLow] <= [IncomingClose]
        AND [IncomingHigh] >= [IncomingOpen]
        AND [IncomingHigh] >= [IncomingClose]
        AND [IncomingLow] <= [IncomingHigh]
        AND [IncomingVolume] >= 0
    ),
    CONSTRAINT [CK_IntradayEvidenceConflict_Code] CHECK (LEN(LTRIM(RTRIM([ConflictCode]))) > 0),
    CONSTRAINT [CK_IntradayEvidenceConflict_Resolution] CHECK
    (
        (
            [ResolutionDisposition] = N'Unresolved'
            AND [ResolvedUtc] IS NULL
            AND [ResolutionReason] IS NULL
        )
        OR
        (
            [ResolutionDisposition] IN (N'CanonicalRetained', N'ProviderEscalated', N'Invalidated')
            AND [ResolvedUtc] IS NOT NULL
            AND [ResolvedUtc] >= [ReceivedUtc]
            AND LEN(LTRIM(RTRIM([ResolutionReason]))) > 0
        )
    )
);
GO

CREATE INDEX [IX_IntradayEvidenceConflict_NaturalKey]
    ON [dbo].[IntradayEvidenceConflict] ([Symbol], [IntervalMinutes], [ExistingBarEventUtc])
    INCLUDE ([ExistingEvidenceBarId], [PollObservationId], [ResolutionDisposition]);
GO
