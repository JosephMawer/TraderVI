CREATE TABLE [dbo].[ShadowCapitalEvent]
(
    [CapitalEventId]         UNIQUEIDENTIFIER NOT NULL,
    [GenerationId]           UNIQUEIDENTIFIER NOT NULL,
    [OccurredUtc]            DATETIME2        NOT NULL,
    [EventType]              NVARCHAR(24)     NOT NULL,
    [TotalAccountValue]      DECIMAL(19,6)    NULL,
    [AvailableAccountCash]   DECIMAL(19,6)    NULL,
    [ExternalFlowAmount]     DECIMAL(19,6)    NULL,
    [Notes]                  NVARCHAR(512)    NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowCapitalEvent_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowCapitalEvent] PRIMARY KEY CLUSTERED ([CapitalEventId]),
    CONSTRAINT [FK_ShadowCapitalEvent_Generation] FOREIGN KEY ([GenerationId]) REFERENCES [dbo].[ShadowPortfolioGeneration] ([GenerationId]),
    CONSTRAINT [CK_ShadowCapitalEvent_Type] CHECK ([EventType] IN (N'InitialSnapshot', N'AccountSnapshot', N'Deposit', N'Withdrawal')),
    CONSTRAINT [CK_ShadowCapitalEvent_Values] CHECK
    (
        ([EventType] IN (N'InitialSnapshot', N'AccountSnapshot')
            AND [TotalAccountValue] > 0
            AND [AvailableAccountCash] >= 0
            AND [AvailableAccountCash] <= [TotalAccountValue]
            AND [ExternalFlowAmount] IS NULL)
        OR ([EventType] = N'Deposit' AND [ExternalFlowAmount] > 0)
        OR ([EventType] = N'Withdrawal' AND [ExternalFlowAmount] < 0)
    )
);
GO

CREATE INDEX [IX_ShadowCapitalEvent_GenerationTime]
    ON [dbo].[ShadowCapitalEvent] ([GenerationId], [OccurredUtc] DESC);
GO

