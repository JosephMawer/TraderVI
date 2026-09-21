-- ADR-0083. Append-only account risk continuation; no baseline creation, history reset or execution.
-- Review individually, obtain explicit installation authority and verify a fresh full backup first.
-- Migration 035 is a prerequisite and remains separately reviewed; do not replay 034 or corrections.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.BrokerAccountRiskBaseline',N'U') IS NULL
 OR OBJECT_ID(N'dbo.BrokerAccountRiskBaseline_Immutable',N'TR') IS NULL
 OR COL_LENGTH(N'dbo.BrokerAccountRiskBaseline',N'BaselineId') IS NULL
 THROW 51671,'Reviewed initial baseline storage (035) is required before account risk continuation.',1;
IF OBJECT_ID(N'dbo.BrokerAccountRiskContinuation') IS NOT NULL
 OR OBJECT_ID(N'dbo.BrokerAccountRiskContinuation_Immutable') IS NOT NULL
 THROW 51672,'Unexpected existing account risk continuation object. Inspect before proceeding.',1;
GO
CREATE TABLE [dbo].[BrokerAccountRiskContinuation]
(
 [Sequence] bigint IDENTITY(1,1) NOT NULL,
 [RequestId] uniqueidentifier NOT NULL,
 [BaselineId] uniqueidentifier NOT NULL,
 [PreviousRevisionId] uniqueidentifier NULL,
 [TradingDate] date NOT NULL,
 [SavedUtc] datetime2(7) NOT NULL,
 [RequestHash] varchar(64) NOT NULL,
 [ContentHash] varchar(64) NOT NULL,
 [ReceiptJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_BrokerAccountRiskContinuation] PRIMARY KEY ([RequestId]),
 CONSTRAINT [UQ_BrokerAccountRiskContinuation_Sequence] UNIQUE ([Sequence]),
 CONSTRAINT [UQ_BrokerAccountRiskContinuation_AccountRequest] UNIQUE ([BaselineId],[RequestId]),
 CONSTRAINT [UQ_BrokerAccountRiskContinuation_Previous] UNIQUE ([BaselineId],[PreviousRevisionId]),
 CONSTRAINT [FK_BrokerAccountRiskContinuation_Baseline] FOREIGN KEY ([BaselineId]) REFERENCES [dbo].[BrokerAccountRiskBaseline]([BaselineId]),
 CONSTRAINT [FK_BrokerAccountRiskContinuation_Previous] FOREIGN KEY ([BaselineId],[PreviousRevisionId]) REFERENCES [dbo].[BrokerAccountRiskContinuation]([BaselineId],[RequestId]),
 CONSTRAINT [CK_BrokerAccountRiskContinuation_Content] CHECK
  (ISJSON([ReceiptJson])=1 AND LEN([RequestHash])=64 AND LEN([ContentHash])=64 AND
   ([PreviousRevisionId] IS NULL OR [PreviousRevisionId]<>[RequestId]))
);
GO

CREATE TRIGGER [dbo].[BrokerAccountRiskContinuation_Immutable] ON [dbo].[BrokerAccountRiskContinuation]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51670, 'Broker account risk history is immutable. Append verified continuation; never reset or rewrite prior risk references.', 1;
END;
GO

IF OBJECT_ID(N'dbo.BrokerAccountRiskContinuation',N'U') IS NULL
 OR OBJECT_ID(N'dbo.BrokerAccountRiskContinuation_Immutable',N'TR') IS NULL
 THROW 51673,'Account risk continuation postconditions were not met.',1;
COMMIT TRANSACTION;
GO