CREATE TABLE [dbo].[EngineMarketCycle]
(
    [Sequence] BIGINT IDENTITY(1,1) NOT NULL,
    [CycleId] UNIQUEIDENTIFIER NOT NULL,
    [SessionId] UNIQUEIDENTIFIER NOT NULL,
    [BarEndUtc] DATETIME2 NOT NULL,
    [ContinuityEpoch] INT NOT NULL,
    [ContextHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [InputIdsJson] NVARCHAR(MAX) NOT NULL,
    [PublishedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineMarketCycle_Published] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineMarketCycle] PRIMARY KEY ([CycleId]),
    CONSTRAINT [UQ_EngineMarketCycle_Sequence] UNIQUE ([Sequence]),
    CONSTRAINT [UQ_EngineMarketCycle_End] UNIQUE ([SessionId],[BarEndUtc]),
    CONSTRAINT [FK_EngineMarketCycle_Collection] FOREIGN KEY ([CycleId]) REFERENCES [dbo].[IntradayCollectionCycle]([CycleId]),
    CONSTRAINT [FK_EngineMarketCycle_Context] FOREIGN KEY ([ContextHash]) REFERENCES [dbo].[EngineEvidenceContent]([ContentHash]),
    CONSTRAINT [CK_EngineMarketCycle_Inputs] CHECK (ISJSON([InputIdsJson])=1 AND [ContinuityEpoch]>0 AND [PublishedUtc]>[BarEndUtc])
);
GO
