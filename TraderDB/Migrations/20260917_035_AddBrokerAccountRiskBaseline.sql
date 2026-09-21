-- ADR-0081. Initial risk-reference storage only; creates no baseline or trading authority.
-- September 17 UTC / September 16 Toronto. Review individually, take/verify a fresh full backup,
-- and obtain explicit operator authorization before installation. Never replay migration 034.
-- Failure before commit rolls back. After commit preserve these objects and any saved baselines;
-- corrections require a separately reviewed migration, never a reset or history deletion.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.BrokerAccountImportBinding',N'U') IS NULL OR OBJECT_ID(N'dbo.BrokerAccountImport',N'U') IS NULL
 OR OBJECT_ID(N'dbo.BrokerAccountImportBinding_Immutable',N'TR') IS NULL OR OBJECT_ID(N'dbo.BrokerAccountImport_Immutable',N'TR') IS NULL
 THROW 51651,'Reviewed migration 034 is required before initial risk-baseline storage.',1;
IF OBJECT_ID(N'dbo.TradingAccountReport',N'U') IS NULL OR OBJECT_ID(N'dbo.TradingRecommendationBatch',N'U') IS NULL
 OR COL_LENGTH(N'dbo.BrokerAccountImport',N'BindingId') IS NULL OR COL_LENGTH(N'dbo.TradingAccountReport',N'AccountLabel') IS NULL
 THROW 51651,'The expected linked-account and recommendation schema is unavailable.',1;
IF OBJECT_ID(N'dbo.BrokerAccountRiskBaseline') IS NOT NULL OR OBJECT_ID(N'dbo.BrokerAccountRiskBaseline_Immutable') IS NOT NULL
 THROW 51652,'Unexpected existing initial risk-baseline object. Inspect before proceeding.',1;
GO
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
CREATE TRIGGER [dbo].[BrokerAccountRiskBaseline_Immutable] ON [dbo].[BrokerAccountRiskBaseline]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51650, 'Initial broker risk baselines are immutable. Preserve setup evidence; subsequent risk events require their own reviewed workflow.', 1;
END;
GO
IF OBJECT_ID(N'dbo.BrokerAccountRiskBaseline',N'U') IS NULL OR OBJECT_ID(N'dbo.BrokerAccountRiskBaseline_Immutable',N'TR') IS NULL
 THROW 51653,'Initial risk-baseline postconditions were not met.',1;
COMMIT TRANSACTION;
GO
