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
