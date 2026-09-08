CREATE TABLE [dbo].[TradingRecommendationBatch]
(
 [BatchId] uniqueidentifier NOT NULL,
 [Sequence] bigint IDENTITY(1,1) NOT NULL,
 [AccountLabel] nvarchar(64) NOT NULL,
 [PromotionId] uniqueidentifier NOT NULL,
 [AccountReportId] uniqueidentifier NULL,
 [PriorBatchId] uniqueidentifier NULL,
 [EvaluatedUtc] datetime2 NOT NULL,
 [InputFingerprint] varchar(64) NOT NULL,
 [InputJson] nvarchar(max) NOT NULL,
 [BatchJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_TradingRecommendationBatch] PRIMARY KEY ([BatchId]),
 CONSTRAINT [UQ_TradingRecommendationBatch_AccountBatch] UNIQUE ([AccountLabel],[BatchId]),
 CONSTRAINT [FK_TradingRecommendationBatch_Promotion] FOREIGN KEY ([AccountLabel],[PromotionId]) REFERENCES [dbo].[TradingPromotion]([AccountLabel],[PromotionId]),
 CONSTRAINT [FK_TradingRecommendationBatch_Report] FOREIGN KEY ([AccountLabel],[AccountReportId]) REFERENCES [dbo].[TradingAccountReport]([AccountLabel],[ReportId]),
 CONSTRAINT [FK_TradingRecommendationBatch_Prior] FOREIGN KEY ([AccountLabel],[PriorBatchId]) REFERENCES [dbo].[TradingRecommendationBatch]([AccountLabel],[BatchId]),
 CONSTRAINT [CK_TradingRecommendationBatch_Json] CHECK(ISJSON([InputJson])=1 AND ISJSON([BatchJson])=1 AND [InputFingerprint]=CONVERT(varchar(64),HASHBYTES('SHA2_256',[InputJson]),2))
);
GO
CREATE INDEX [IX_TradingRecommendationBatch_Account] ON [dbo].[TradingRecommendationBatch]([AccountLabel],[Sequence] DESC);
GO
