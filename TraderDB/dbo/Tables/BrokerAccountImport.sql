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
