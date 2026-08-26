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
GO
