CREATE TABLE [dbo].[TradingReportedFill]
(
 [RequestId] uniqueidentifier NOT NULL,
 [TradeId] uniqueidentifier NOT NULL,
 [BatchId] uniqueidentifier NOT NULL,
 [FillUtc] datetime2 NOT NULL,
 [RecommendationKind] nvarchar(24) NOT NULL,
 [PositionTarget] decimal(19,6) NULL,
 [RequestHash] varchar(64) NOT NULL,
 [ReportJson] nvarchar(max) NOT NULL,
 [ResultJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_TradingReportedFill] PRIMARY KEY ([RequestId]),
 CONSTRAINT [UQ_TradingReportedFill_Trade] UNIQUE ([TradeId]),
 CONSTRAINT [FK_TradingReportedFill_Trade] FOREIGN KEY ([TradeId]) REFERENCES [dbo].[TradeLog]([TradeId]),
 CONSTRAINT [FK_TradingReportedFill_Batch] FOREIGN KEY ([BatchId]) REFERENCES [dbo].[TradingRecommendationBatch]([BatchId]),
 CONSTRAINT [CK_TradingReportedFill_Kind] CHECK([RecommendationKind] IN(N'Initial',N'Reentry',N'AddOn')),
 CONSTRAINT [CK_TradingReportedFill_Json] CHECK(ISJSON([ReportJson])=1 AND ISJSON([ResultJson])=1 AND LEN([RequestHash])=64 AND ([PositionTarget] IS NULL OR [PositionTarget]>0))
);
GO
