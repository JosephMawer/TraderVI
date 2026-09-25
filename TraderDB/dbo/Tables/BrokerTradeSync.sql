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
