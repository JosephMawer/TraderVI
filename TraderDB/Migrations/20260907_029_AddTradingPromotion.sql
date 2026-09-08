-- ADR-0062. Manual, individually reviewed migration. Does not seed approval, reports, accounts or trades.
-- Stop older hosts and create/verify a fresh full backup before a separately authorized SQLCMD -b execution.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.OperatorExitRequest',N'U') IS NULL OR OBJECT_ID(N'dbo.StrategyModelBinding',N'U') IS NULL
 THROW 51520,'Migrations 026 through 028 must be installed first.',1;
IF OBJECT_ID(N'dbo.TradingStrategyVersion') IS NOT NULL THROW 51521,'Unexpected existing promotion object; inspect before proceeding.',1;
IF OBJECT_ID(N'dbo.TradingAccountReport') IS NOT NULL THROW 51521,'Unexpected existing promotion object; inspect before proceeding.',1;
IF OBJECT_ID(N'dbo.TradingPromotion') IS NOT NULL THROW 51521,'Unexpected existing promotion object; inspect before proceeding.',1;
IF OBJECT_ID(N'dbo.TradingRecommendationBatch') IS NOT NULL THROW 51521,'Unexpected existing promotion object; inspect before proceeding.',1;
IF OBJECT_ID(N'dbo.TradingReportedFill') IS NOT NULL THROW 51521,'Unexpected existing promotion object; inspect before proceeding.',1;
GO
CREATE TABLE [dbo].[TradingStrategyVersion]
(
 [VersionId] uniqueidentifier NOT NULL,
 [StrategyId] uniqueidentifier NOT NULL,
 [DailyStrategyVersionId] uniqueidentifier NOT NULL,
 [Family] nvarchar(24) NOT NULL,
 [Fingerprint] varchar(64) NOT NULL,
 [DefinitionJson] nvarchar(max) NOT NULL,
 [CreatedUtc] datetime2 NOT NULL CONSTRAINT [DF_TradingStrategyVersion_Created] DEFAULT SYSUTCDATETIME(),
 CONSTRAINT [PK_TradingStrategyVersion] PRIMARY KEY ([VersionId]),
 CONSTRAINT [FK_TradingStrategyVersion_Daily] FOREIGN KEY ([DailyStrategyVersionId]) REFERENCES [dbo].[StrategyVersion]([VersionId]),
 CONSTRAINT [UQ_TradingStrategyVersion_Fingerprint] UNIQUE ([Fingerprint]),
 CONSTRAINT [CK_TradingStrategyVersion_Family] CHECK ([Family] IN(N'DailyShadow',N'DelphiLive')),
 CONSTRAINT [CK_TradingStrategyVersion_Json] CHECK (ISJSON([DefinitionJson])=1 AND LEN([Fingerprint])=64)
);
GO
CREATE TABLE [dbo].[TradingAccountReport]
(
 [ReportId] uniqueidentifier NOT NULL,
 [Sequence] bigint IDENTITY(1,1) NOT NULL,
 [AccountLabel] nvarchar(64) NOT NULL,
 [TradingDate] date NOT NULL,
 [ReportedUtc] datetime2 NOT NULL,
 [ReportedBy] nvarchar(128) NOT NULL,
 [LedgerFingerprint] varchar(64) NOT NULL,
 [RequestHash] varchar(64) NOT NULL,
 [ReportJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_TradingAccountReport] PRIMARY KEY ([ReportId]),
 CONSTRAINT [UQ_TradingAccountReport_AccountReport] UNIQUE ([AccountLabel],[ReportId]),
 CONSTRAINT [CK_TradingAccountReport_Json] CHECK (ISJSON([ReportJson])=1 AND LEN([AccountLabel])>0 AND LEN([ReportedBy])>0 AND LEN([LedgerFingerprint])=64 AND LEN([RequestHash])=64)
);
GO
CREATE INDEX [IX_TradingAccountReport_Account] ON [dbo].[TradingAccountReport]([AccountLabel],[Sequence] DESC);
GO
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
CREATE TRIGGER [dbo].[TradingStrategyVersion_Immutable] ON [dbo].[TradingStrategyVersion]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51530,'TradingStrategyVersion is append-only; preserve the original history.',1;
END;
GO

CREATE TRIGGER [dbo].[TradingAccountReport_Immutable] ON [dbo].[TradingAccountReport]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51531,'TradingAccountReport is append-only; preserve the original history.',1;
END;
GO

CREATE TRIGGER [dbo].[TradingPromotion_Immutable] ON [dbo].[TradingPromotion]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51532,'TradingPromotion is append-only; preserve the original history.',1;
END;
GO

CREATE TRIGGER [dbo].[TradingRecommendationBatch_Immutable] ON [dbo].[TradingRecommendationBatch]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51533,'TradingRecommendationBatch is append-only; preserve the original history.',1;
END;
GO

CREATE TRIGGER [dbo].[TradingReportedFill_Immutable] ON [dbo].[TradingReportedFill]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51534,'TradingReportedFill is append-only; preserve the original history.',1;
END;
GO
COMMIT TRANSACTION;
GO
