CREATE TABLE [dbo].[TradingPromotion]
(
 [PromotionId] uniqueidentifier NOT NULL,
 [Sequence] bigint IDENTITY(1,1) NOT NULL,
 [AccountLabel] nvarchar(64) NOT NULL,
 [StrategyVersionId] uniqueidentifier NOT NULL,
 [PaperAccountId] uniqueidentifier NOT NULL,
 [PriorPromotionId] uniqueidentifier NULL,
 [AccountReportId] uniqueidentifier NOT NULL,
 [EffectiveUtc] datetime2 NOT NULL,
 [Reviewer] nvarchar(128) NOT NULL,
 [Reason] nvarchar(1024) NOT NULL,
 [RequestHash] varchar(64) NOT NULL,
 [ReviewJson] nvarchar(max) NOT NULL,
 [AssignmentJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_TradingPromotion] PRIMARY KEY ([PromotionId]),
 CONSTRAINT [UQ_TradingPromotion_AccountPromotion] UNIQUE ([AccountLabel],[PromotionId]),
 CONSTRAINT [UQ_TradingPromotion_Prior] UNIQUE ([AccountLabel],[PriorPromotionId]),
 CONSTRAINT [UQ_TradingPromotion_Boundary] UNIQUE ([AccountLabel],[EffectiveUtc]),
 CONSTRAINT [FK_TradingPromotion_Strategy] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[TradingStrategyVersion]([VersionId]),
 CONSTRAINT [FK_TradingPromotion_Prior] FOREIGN KEY ([AccountLabel],[PriorPromotionId]) REFERENCES [dbo].[TradingPromotion]([AccountLabel],[PromotionId]),
 CONSTRAINT [FK_TradingPromotion_Report] FOREIGN KEY ([AccountLabel],[AccountReportId]) REFERENCES [dbo].[TradingAccountReport]([AccountLabel],[ReportId]),
 CONSTRAINT [CK_TradingPromotion_Json] CHECK(ISJSON([ReviewJson])=1 AND ISJSON([AssignmentJson])=1 AND LEN([RequestHash])=64 AND LEN([Reviewer])>0 AND LEN([Reason])>0)
);
GO
CREATE INDEX [IX_TradingPromotion_Account] ON [dbo].[TradingPromotion]([AccountLabel],[Sequence] DESC);
GO
