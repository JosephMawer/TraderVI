-- Additive only. The queue starts empty. Install before the rule-driven collector and consumer.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
    WHERE b.database_name=DB_NAME() AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
    AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
    THROW 54646,'A fresh checksum full backup is required for collector history requests.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.MarketCollectorHistoryNeed') IS NOT NULL
    OR COL_LENGTH(N'dbo.MarketCollectorRequest',N'PackedContent') IS NULL
    OR OBJECT_ID(N'dbo.TR_MarketCollectorAttempt_Immutable',N'TR') IS NULL
    THROW 54647,'Expected installed collector schema 056 and no existing history queue.',1;
CREATE TABLE [dbo].[MarketCollectorHistoryNeed]
(
    [SourceContract] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SessionOpenUtc] datetime2 NOT NULL,
    [BarStartUtc] datetime2 NOT NULL,
    [Rule] varchar(24) NOT NULL,
    [ExpiresUtc] datetime2 NOT NULL,
    [RequestId] uniqueidentifier NULL,
    CONSTRAINT [PK_MarketCollectorHistoryNeed] PRIMARY KEY ([SourceContract],[SessionOpenUtc],[Symbol],[BarStartUtc]),
    CONSTRAINT [FK_MarketCollectorHistoryNeed_Attempt] FOREIGN KEY ([RequestId]) REFERENCES [dbo].[MarketCollectorAttempt]([RequestId]),
    CONSTRAINT [CK_MarketCollectorHistoryNeed_Window] CHECK ([BarStartUtc]>=[SessionOpenUtc] AND
        [ExpiresUtc]>DATEADD(MINUTE,1,[BarStartUtc]) AND [ExpiresUtc]<=DATEADD(DAY,1,[SessionOpenUtc]) AND
        DATEPART(SECOND,[BarStartUtc])=0 AND DATEPART(NANOSECOND,[BarStartUtc])=0 AND [Rule] IN ('MinuteEntry','HoldingClose'))
);
GO

CREATE INDEX [IX_MarketCollectorHistoryNeed_Request] ON [dbo].[MarketCollectorHistoryNeed]([RequestId]);
GO
COMMIT TRANSACTION;
GO
