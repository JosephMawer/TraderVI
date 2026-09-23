CREATE TABLE [dbo].[BrokerManagedSessionRevision]
(
 [BindingId] uniqueidentifier NOT NULL,
 [Revision] bigint NOT NULL,
 [RevisionId] uniqueidentifier NOT NULL,
 [PreviousRevisionId] uniqueidentifier NULL,
 [SessionId] uniqueidentifier NOT NULL,
 [BaselineId] uniqueidentifier NOT NULL,
 [SavedAt] datetimeoffset(7) NOT NULL,
 [ContentHash] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 [SnapshotJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_BrokerManagedSessionRevision] PRIMARY KEY ([BindingId],[Revision]),
 CONSTRAINT [UQ_BrokerManagedSessionRevision_Id] UNIQUE ([RevisionId]),
 CONSTRAINT [UQ_BrokerManagedSessionRevision_AccountId] UNIQUE ([BindingId],[RevisionId]),
 CONSTRAINT [UQ_BrokerManagedSessionRevision_Previous] UNIQUE ([BindingId],[PreviousRevisionId]),
 CONSTRAINT [FK_BrokerManagedSessionRevision_Binding] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[BrokerAccountImportBinding]([BindingId]),
 CONSTRAINT [FK_BrokerManagedSessionRevision_Baseline] FOREIGN KEY ([BaselineId]) REFERENCES [dbo].[BrokerAccountRiskBaseline]([BaselineId]),
 CONSTRAINT [FK_BrokerManagedSessionRevision_Previous] FOREIGN KEY ([BindingId],[PreviousRevisionId]) REFERENCES [dbo].[BrokerManagedSessionRevision]([BindingId],[RevisionId]),
 CONSTRAINT [CK_BrokerManagedSessionRevision_Content] CHECK
  (ISJSON([SnapshotJson])=1 AND LEN([ContentHash])=64 AND [ContentHash] NOT LIKE '%[^0-9A-F]%'
   AND [Revision]>=0 AND (([Revision]=0 AND [PreviousRevisionId] IS NULL) OR ([Revision]>0 AND [PreviousRevisionId] IS NOT NULL)))
);
GO
