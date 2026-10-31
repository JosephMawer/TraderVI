-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingSymbolCurrent]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [Symbol] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [IsRelevant] bit NOT NULL,
    [StrategyId] uniqueidentifier NOT NULL,
    [TradingDate] date NOT NULL,
    [SessionId] uniqueidentifier NOT NULL,
    [EntriesToday] int NOT NULL,
    [LastExitUtc] datetime2(7) NULL,
    [LastExitReason] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    [LastProcessedUtc] datetime2(7) NULL,
    [LastInputId] uniqueidentifier NULL,
    [ConfirmationStartedUtc] datetime2(7) NULL,
    [SignalExpiresUtc] datetime2(7) NULL,
    [ProgressJson] nvarchar(1024) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_TradingSymbolCurrent] PRIMARY KEY ([BindingId],[Symbol]),
    CONSTRAINT [CK_TradingSymbolCurrent_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingSymbolCurrent_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId]),
    CONSTRAINT [CK_TradingSymbolCurrent_1] CHECK ([EntriesToday]>=0),
    CONSTRAINT [CK_TradingSymbolCurrent_2] CHECK ([SignalExpiresUtc] IS NULL OR [ConfirmationStartedUtc] IS NULL OR [SignalExpiresUtc]>=[ConfirmationStartedUtc]),
    CONSTRAINT [CK_TradingSymbolCurrent_3] CHECK (ISJSON([ProgressJson])=1 AND LEFT(LTRIM([ProgressJson]),1)=N'{'),
    CONSTRAINT [CK_TradingSymbolCurrent_Symbol] CHECK (LEN([Symbol])>0 AND [Symbol]=UPPER([Symbol]))
);
GO
