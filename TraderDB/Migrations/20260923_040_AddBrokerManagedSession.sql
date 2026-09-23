-- ADR-0084. Source only: review and separately authorize after a verified full backup.
-- Adds empty session history; does not establish baselines, select a strategy, submit orders or enable a host.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54004,'A fresh checksum full backup of TraderDB is required.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.BrokerAccountRiskBaseline',N'U') IS NULL OR OBJECT_ID(N'dbo.BrokerAccountRiskContinuation',N'U') IS NULL
 THROW 54005,'Individually reviewed migrations 035 and 039 must be installed first.',1;
IF OBJECT_ID(N'dbo.BrokerManagedSessionRevision') IS NOT NULL OR OBJECT_ID(N'dbo.BrokerManagedSessionRevision_Immutable') IS NOT NULL
 THROW 54006,'Unexpected existing broker session objects; inspect instead of replaying this migration.',1;
GO
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

CREATE TRIGGER [dbo].[BrokerManagedSessionRevision_Immutable] ON [dbo].[BrokerManagedSessionRevision]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 54003, 'Broker strategy sessions are immutable. Append a reviewed state; never remove order intent or recovery history.', 1;
END;
GO

COMMIT TRANSACTION;
GO
