-- ADR-0084. Add durable receipts for atomic broker-order projection into the real ledger.
-- Individually review, obtain explicit installation authority and verify a fresh full backup.
-- No trades are recorded by this migration. Migrations 035/039 are not prerequisites.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.BrokerAccountImport',N'U') IS NULL
 OR OBJECT_ID(N'dbo.BrokerAccountImportBinding',N'U') IS NULL
 OR OBJECT_ID(N'dbo.BrokerAccountImport_Immutable',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TradeLog',N'U') IS NULL OR OBJECT_ID(N'dbo.ActivePosition',N'U') IS NULL
 THROW 51691,'Broker import migration 034 and the real trade ledger are required.',1;
IF OBJECT_ID(N'dbo.BrokerTradeSync') IS NOT NULL OR OBJECT_ID(N'dbo.BrokerTradeSync_Immutable') IS NOT NULL
 THROW 51692,'Unexpected synchronization objects. Inspect before proceeding.',1;
GO
CREATE TABLE [dbo].[BrokerTradeSync]
(
 [Sequence] bigint IDENTITY(1,1) NOT NULL,
 [ImportId] uniqueidentifier NOT NULL,
 [BindingId] uniqueidentifier NOT NULL,
 [PreviousImportId] uniqueidentifier NULL,
 [ContentHash] varchar(64) NOT NULL,
 [ReceiptJson] nvarchar(max) NOT NULL,
 [SavedUtc] datetime2(7) NOT NULL,
 CONSTRAINT [PK_BrokerTradeSync] PRIMARY KEY ([ImportId]),
 CONSTRAINT [UQ_BrokerTradeSync_BindingImport] UNIQUE ([BindingId],[ImportId]),
 CONSTRAINT [UQ_BrokerTradeSync_Previous] UNIQUE ([BindingId],[PreviousImportId]),
 CONSTRAINT [FK_BrokerTradeSync_Import] FOREIGN KEY ([BindingId],[ImportId]) REFERENCES [dbo].[BrokerAccountImport]([BindingId],[RequestId]),
 CONSTRAINT [FK_BrokerTradeSync_Previous] FOREIGN KEY ([BindingId],[PreviousImportId]) REFERENCES [dbo].[BrokerTradeSync]([BindingId],[ImportId]),
 CONSTRAINT [CK_BrokerTradeSync_Content] CHECK (ISJSON([ReceiptJson])=1 AND LEN([ContentHash])=64 AND
   ([PreviousImportId] IS NULL OR [PreviousImportId]<>[ImportId]))
);
GO
CREATE INDEX [IX_BrokerTradeSync_Latest] ON [dbo].[BrokerTradeSync]([BindingId],[Sequence] DESC);
GO

CREATE TRIGGER [dbo].[BrokerTradeSync_Immutable] ON [dbo].[BrokerTradeSync]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51690, 'Broker trade synchronization receipts are immutable. Review corrections explicitly.', 1;
END;
GO

IF OBJECT_ID(N'dbo.BrokerTradeSync',N'U') IS NULL OR OBJECT_ID(N'dbo.BrokerTradeSync_Immutable',N'TR') IS NULL
 THROW 51693,'Broker trade synchronization postconditions failed.',1;
COMMIT TRANSACTION;
GO
