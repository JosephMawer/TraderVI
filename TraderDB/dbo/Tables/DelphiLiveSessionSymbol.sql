CREATE TABLE [dbo].[DelphiLiveSessionSymbol]
(
    [SessionSymbolId]             UNIQUEIDENTIFIER NOT NULL,
    [SessionId]                   UNIQUEIDENTIFIER NOT NULL,
    [Symbol]                      NVARCHAR(20)     NOT NULL,
    [IsFrozenDailyCandidate]      BIT              NOT NULL,
    [IsXiuBenchmark]              BIT              NOT NULL,
    [IsTrackedHolding]            BIT              NOT NULL,
    [IsDelphiLiveHolding]         BIT              NOT NULL,
    [HasPendingProtectiveSell]    BIT              NOT NULL,
    [IsSessionCarryCandidate]     BIT              NOT NULL,
    [FrozenSourceLensCount]       TINYINT          NOT NULL,
    [BestFrozenSourceLensRank]    TINYINT          NULL,
    [RequiredFromBarEndUtc]       DATETIME2        NOT NULL,
    [RequiredThroughBarEndUtc]    DATETIME2        NOT NULL,
    [SourceIdentityJson]          NVARCHAR(MAX)    NOT NULL,
    [AddedUtc]                    DATETIME2        NOT NULL,
    [CreatedUtc]                  DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveSessionSymbol_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLiveSessionSymbol] PRIMARY KEY CLUSTERED ([SessionSymbolId]),
    CONSTRAINT [FK_DelphiLiveSessionSymbol_Session] FOREIGN KEY ([SessionId])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId]),
    CONSTRAINT [FK_DelphiLiveSessionSymbol_Symbol] FOREIGN KEY ([Symbol])
        REFERENCES [dbo].[Symbols] ([Symbol]),
    CONSTRAINT [UQ_DelphiLiveSessionSymbol_Symbol] UNIQUE ([SessionId], [Symbol]),
    CONSTRAINT [UQ_DelphiLiveSessionSymbol_Identity] UNIQUE ([SessionSymbolId], [SessionId]),
    CONSTRAINT [UQ_DelphiLiveSessionSymbol_SlotIdentity] UNIQUE ([SessionSymbolId], [SessionId], [Symbol]),
    CONSTRAINT [CK_DelphiLiveSessionSymbol_Source] CHECK
    (
        [IsFrozenDailyCandidate] = 1
        OR [IsXiuBenchmark] = 1
        OR [IsTrackedHolding] = 1
        OR [IsDelphiLiveHolding] = 1
        OR [HasPendingProtectiveSell] = 1
        OR [IsSessionCarryCandidate] = 1
    ),
    CONSTRAINT [CK_DelphiLiveSessionSymbol_FrozenLens] CHECK
    (
        (
            [IsFrozenDailyCandidate] = 1
            AND [FrozenSourceLensCount] IN (1, 2)
            AND [BestFrozenSourceLensRank] BETWEEN 1 AND 25
        )
        OR
        (
            [IsFrozenDailyCandidate] = 0
            AND [FrozenSourceLensCount] = 0
            AND [BestFrozenSourceLensRank] IS NULL
        )
    ),
    CONSTRAINT [CK_DelphiLiveSessionSymbol_Benchmark] CHECK
    (
        [IsXiuBenchmark] = 0
        OR
        (
            [Symbol] = N'XIU'
            AND [IsFrozenDailyCandidate] = 0
            AND [IsSessionCarryCandidate] = 0
        )
    ),
    CONSTRAINT [CK_DelphiLiveSessionSymbol_Range] CHECK
    (
        [RequiredFromBarEndUtc] <= [RequiredThroughBarEndUtc]
        AND [AddedUtc] <= [RequiredThroughBarEndUtc]
        AND ISJSON([SourceIdentityJson]) = 1
    )
);
GO

CREATE INDEX [IX_DelphiLiveSessionSymbol_RequiredRange]
    ON [dbo].[DelphiLiveSessionSymbol] ([SessionId], [RequiredFromBarEndUtc], [RequiredThroughBarEndUtc])
    INCLUDE ([Symbol], [IsXiuBenchmark], [HasPendingProtectiveSell], [IsDelphiLiveHolding]);
GO
