CREATE TABLE [dbo].[EngineConsumerReceipt]
(
    [Consumer] NVARCHAR(16) NOT NULL,
    [CycleId] UNIQUEIDENTIFIER NOT NULL,
    [Outcome] NVARCHAR(32) NOT NULL,
    [CompletedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineConsumerReceipt_Completed] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineConsumerReceipt] PRIMARY KEY ([Consumer],[CycleId]),
    CONSTRAINT [FK_EngineConsumerReceipt_Cycle] FOREIGN KEY ([CycleId]) REFERENCES [dbo].[EngineMarketCycle]([CycleId]),
    CONSTRAINT [CK_EngineConsumerReceipt_Consumer] CHECK ([Consumer] IN(N'Paper',N'Research')),
    CONSTRAINT [CK_EngineConsumerReceipt_Outcome] CHECK ([Outcome] IN(N'Applied',N'NotActionableLate',N'DefinitionChanged',N'EvidenceConflict',N'ResearchRecorded',N'ResearchNotApplicable'))
);
GO
