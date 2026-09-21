-- ADR-0080. Additive account-observation import only; does not seed accounts or activate trading.
-- Apply only after individual review, a fresh verified full backup, and explicit operator authorization.
-- A failure before commit rolls back this transaction. After commit, preserve these objects and any
-- observations; use a separately reviewed corrective migration rather than deleting account history.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.TradingAccountReport',N'U') IS NULL OR OBJECT_ID(N'dbo.TradingReportedFill',N'U') IS NULL
 THROW 51642,'Migration 029 is required before broker history import.',1;
IF COL_LENGTH(N'dbo.ActivePosition',N'AccountLabel') IS NULL OR COL_LENGTH(N'dbo.TradeLog',N'NetAmount') IS NULL
 THROW 51642,'The expected tracked-account schema is unavailable.',1;
IF OBJECT_ID(N'dbo.BrokerAccountImportBinding') IS NOT NULL OR OBJECT_ID(N'dbo.BrokerAccountImport') IS NOT NULL
 OR OBJECT_ID(N'dbo.BrokerAccountImportBinding_Immutable') IS NOT NULL OR OBJECT_ID(N'dbo.BrokerAccountImport_Immutable') IS NOT NULL
 THROW 51643,'Unexpected existing broker import object. Inspect before proceeding.',1;
GO
CREATE TABLE [dbo].[BrokerAccountImportBinding]
(
 [BindingId] uniqueidentifier NOT NULL,
 [AccountLabel] nvarchar(64) NOT NULL,
 [BrokerKey] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 [LegacyFingerprint] varchar(64) NOT NULL,
 [CreatedUtc] datetime2 NOT NULL,
 [ReviewedBy] nvarchar(128) NOT NULL,
 CONSTRAINT [PK_BrokerAccountImportBinding] PRIMARY KEY ([BindingId]),
 CONSTRAINT [UQ_BrokerAccountImportBinding_Account] UNIQUE ([AccountLabel]),
 CONSTRAINT [UQ_BrokerAccountImportBinding_Broker] UNIQUE ([BrokerKey]),
 CONSTRAINT [CK_BrokerAccountImportBinding_Identity] CHECK
  ([AccountLabel]=N'TFSA' AND LEN([BrokerKey])=64 AND LEN([LegacyFingerprint])=64 AND LEN(LTRIM(RTRIM([ReviewedBy])))>0)
);
GO

CREATE TABLE [dbo].[BrokerAccountImport]
(
 [RequestId] uniqueidentifier NOT NULL,
 [Sequence] bigint IDENTITY(1,1) NOT NULL,
 [BindingId] uniqueidentifier NOT NULL,
 [PriorRequestId] uniqueidentifier NULL,
 [RequestHash] varchar(64) NOT NULL,
 [PlanFingerprint] varchar(64) NOT NULL,
 [SnapshotUtc] datetime2 NOT NULL,
 [SavedUtc] datetime2 NOT NULL,
 [ReviewedBy] nvarchar(128) NOT NULL,
 [PlanJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_BrokerAccountImport] PRIMARY KEY ([RequestId]),
 CONSTRAINT [UQ_BrokerAccountImport_BindingRequest] UNIQUE ([BindingId],[RequestId]),
 CONSTRAINT [UQ_BrokerAccountImport_Previous] UNIQUE ([BindingId],[PriorRequestId]),
 CONSTRAINT [FK_BrokerAccountImport_Binding] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[BrokerAccountImportBinding]([BindingId]),
 CONSTRAINT [FK_BrokerAccountImport_Previous] FOREIGN KEY ([BindingId],[PriorRequestId]) REFERENCES [dbo].[BrokerAccountImport]([BindingId],[RequestId]),
 CONSTRAINT [CK_BrokerAccountImport_Content] CHECK
  (ISJSON([PlanJson])=1 AND LEN([RequestHash])=64 AND LEN([PlanFingerprint])=64 AND LEN(LTRIM(RTRIM([ReviewedBy])))>0 AND [SnapshotUtc]<=[SavedUtc])
);
GO
CREATE INDEX [IX_BrokerAccountImport_Latest] ON [dbo].[BrokerAccountImport]([BindingId],[Sequence] DESC);
GO

CREATE TRIGGER [dbo].[BrokerAccountImportBinding_Immutable] ON [dbo].[BrokerAccountImportBinding]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51640, 'Broker account import bindings are immutable. Relinking requires a separately reviewed migration.', 1;
END;
GO

CREATE TRIGGER [dbo].[BrokerAccountImport_Immutable] ON [dbo].[BrokerAccountImport]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51641, 'Broker account import observations are immutable. Append a reviewed observation; preserve existing history.', 1;
END;
GO

IF OBJECT_ID(N'dbo.BrokerAccountImportBinding',N'U') IS NULL OR OBJECT_ID(N'dbo.BrokerAccountImport',N'U') IS NULL
 OR OBJECT_ID(N'dbo.BrokerAccountImportBinding_Immutable',N'TR') IS NULL OR OBJECT_ID(N'dbo.BrokerAccountImport_Immutable',N'TR') IS NULL
 THROW 51644,'Broker import postconditions were not met.',1;
COMMIT TRANSACTION;
GO
