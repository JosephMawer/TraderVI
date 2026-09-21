CREATE TABLE [dbo].[BrokerAccountRiskBaseline]
(
 [BaselineId] uniqueidentifier NOT NULL,
 [BindingId] uniqueidentifier NOT NULL,
 [AccountLabel] nvarchar(64) NOT NULL,
 [ImportRequestId] uniqueidentifier NOT NULL,
 [AccountReportId] uniqueidentifier NULL,
 [RecommendationBatchId] uniqueidentifier NULL,
 [TradingDate] date NOT NULL,
 [EstablishedUtc] datetime2(7) NOT NULL,
 [ReviewedBy] nvarchar(128) NOT NULL,
 [RequestHash] varchar(64) NOT NULL,
 [ContentHash] varchar(64) NOT NULL,
 [BaselineJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_BrokerAccountRiskBaseline] PRIMARY KEY ([BaselineId]),
 CONSTRAINT [UQ_BrokerAccountRiskBaseline_Binding] UNIQUE ([BindingId]),
 CONSTRAINT [UQ_BrokerAccountRiskBaseline_Account] UNIQUE ([AccountLabel]),
 CONSTRAINT [FK_BrokerAccountRiskBaseline_Import] FOREIGN KEY ([BindingId],[ImportRequestId]) REFERENCES [dbo].[BrokerAccountImport]([BindingId],[RequestId]),
 CONSTRAINT [FK_BrokerAccountRiskBaseline_Report] FOREIGN KEY ([AccountLabel],[AccountReportId]) REFERENCES [dbo].[TradingAccountReport]([AccountLabel],[ReportId]),
 CONSTRAINT [FK_BrokerAccountRiskBaseline_Recommendation] FOREIGN KEY ([AccountLabel],[RecommendationBatchId]) REFERENCES [dbo].[TradingRecommendationBatch]([AccountLabel],[BatchId]),
 CONSTRAINT [CK_BrokerAccountRiskBaseline_Content] CHECK
  ([AccountLabel]=N'TFSA' AND ISJSON([BaselineJson])=1 AND LEN([RequestHash])=64 AND LEN([ContentHash])=64 AND LEN(LTRIM(RTRIM([ReviewedBy])))>0)
);
GO
