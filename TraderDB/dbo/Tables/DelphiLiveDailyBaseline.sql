CREATE TABLE [dbo].[DelphiLiveDailyBaseline]
(
    [DailyBaselineId]             UNIQUEIDENTIFIER NOT NULL,
    [SessionId]                   UNIQUEIDENTIFIER NOT NULL,
    [SessionSymbolId]             UNIQUEIDENTIFIER NOT NULL,
    [BaselineDefinition]          NVARCHAR(64)     NOT NULL,
    [BaselineSchemaVersion]       INT              NOT NULL,
    [SourceThroughTradingDate]    DATE             NOT NULL,
    [AlignedDailyBarCount]        INT              NOT NULL,
    [PreviousCanonicalClose]      DECIMAL(19,6)    NULL,
    [MedianTrueRangePct5]         DECIMAL(28,12)   NULL,
    [MedianTrueRangePct10]        DECIMAL(28,12)   NULL,
    [MedianTrueRangePct14]        DECIMAL(28,12)   NULL,
    [MedianTrueRangePct20]        DECIMAL(28,12)   NULL,
    [MedianFullDayVolume20]       DECIMAL(28,6)    NULL,
    [AlignedDailyBarsJson]        NVARCHAR(MAX)    NOT NULL,
    [AuditState]                  NVARCHAR(16)     NOT NULL,
    [AuditCode]                   NVARCHAR(64)     NULL,
    [FrozenUtc]                   DATETIME2        NOT NULL,
    [CreatedUtc]                  DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveDailyBaseline_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLiveDailyBaseline] PRIMARY KEY CLUSTERED ([DailyBaselineId]),
    CONSTRAINT [FK_DelphiLiveDailyBaseline_SessionSymbol] FOREIGN KEY ([SessionSymbolId], [SessionId])
        REFERENCES [dbo].[DelphiLiveSessionSymbol] ([SessionSymbolId], [SessionId]),
    CONSTRAINT [FK_DelphiLiveDailyBaseline_SourceThroughDate] FOREIGN KEY ([SessionId], [SourceThroughTradingDate])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId], [ExpectedPriorCanonicalSessionDate]),
    CONSTRAINT [UQ_DelphiLiveDailyBaseline_SessionSymbol] UNIQUE ([SessionSymbolId]),
    CONSTRAINT [CK_DelphiLiveDailyBaseline_Definition] CHECK
    (
        [BaselineDefinition] = N'DelphiLiveDailyBaselineV1'
        AND [BaselineSchemaVersion] = 1
        AND [AlignedDailyBarCount] BETWEEN 0 AND 21
        AND ISJSON([AlignedDailyBarsJson]) = 1
    ),
    CONSTRAINT [CK_DelphiLiveDailyBaseline_AuditState] CHECK ([AuditState] IN (N'Valid', N'Unavailable', N'Invalid')),
    CONSTRAINT [CK_DelphiLiveDailyBaseline_Valid] CHECK
    (
        (
            [AuditState] = N'Valid'
            AND [AuditCode] IS NULL
            AND [AlignedDailyBarCount] = 21
            AND [PreviousCanonicalClose] > 0
            AND [MedianTrueRangePct5] > 0
            AND [MedianTrueRangePct10] > 0
            AND [MedianTrueRangePct14] > 0
            AND [MedianTrueRangePct20] > 0
            AND [MedianFullDayVolume20] > 0
        )
        OR
        (
            [AuditState] IN (N'Unavailable', N'Invalid')
            AND LEN(LTRIM(RTRIM([AuditCode]))) > 0
        )
    )
);
GO

CREATE INDEX [IX_DelphiLiveDailyBaseline_SessionState]
    ON [dbo].[DelphiLiveDailyBaseline] ([SessionId], [AuditState])
    INCLUDE ([SessionSymbolId], [SourceThroughTradingDate], [MedianTrueRangePct10]);
GO
