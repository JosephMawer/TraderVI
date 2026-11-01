-- Coordinate the collector-writer cutover with this migration. Existing rows stay in place.
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
    THROW 54640,'A fresh checksum full backup is required for collector receipt storage.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.MarketCollectorRequest',N'U') IS NULL
    OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.MarketCollectorRequest')
        AND name=N'ContentJson' AND system_type_id=231 AND max_length=-1 AND is_nullable=0 AND is_computed=0)
    OR COL_LENGTH(N'dbo.MarketCollectorRequest',N'PlainContentJson') IS NOT NULL
    OR COL_LENGTH(N'dbo.MarketCollectorRequest',N'PackedContent') IS NOT NULL
    OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.MarketCollectorRequest')
        AND name=N'CK_MarketCollectorRequest_Content' AND is_disabled=0 AND is_not_trusted=0)
    OR NOT EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id=OBJECT_ID(N'dbo.MarketCollectorRequest')
        AND name=N'TR_MarketCollectorRequest_Immutable' AND is_instead_of_trigger=1 AND is_disabled=0)
    THROW 54641,'Expected the original 052 collector receipt table and enabled guards.',1;
IF EXISTS (SELECT 1 FROM dbo.MarketCollectorLease WITH (TABLOCKX,HOLDLOCK) WHERE ExpiresUtc>SYSUTCDATETIME())
    THROW 54642,'Stop the collector and wait for its writer lease to expire.',1;
ALTER TABLE dbo.MarketCollectorRequest DROP CONSTRAINT CK_MarketCollectorRequest_Content;
EXEC sys.sp_rename N'dbo.MarketCollectorRequest.ContentJson', N'PlainContentJson', N'COLUMN';
GO
ALTER TABLE dbo.MarketCollectorRequest ALTER COLUMN PlainContentJson nvarchar(max) NULL;
ALTER TABLE dbo.MarketCollectorRequest ADD PackedContent varbinary(max) NULL;
GO
ALTER TABLE dbo.MarketCollectorRequest ADD ContentJson AS
    (COALESCE(PlainContentJson,CONVERT(nvarchar(max),DECOMPRESS(PackedContent))));
GO
ALTER TABLE dbo.MarketCollectorRequest WITH CHECK ADD CONSTRAINT CK_MarketCollectorRequest_Encoding CHECK
    ((PlainContentJson IS NULL AND PackedContent IS NOT NULL) OR
     (PlainContentJson IS NOT NULL AND PackedContent IS NULL));
ALTER TABLE dbo.MarketCollectorRequest WITH CHECK ADD CONSTRAINT CK_MarketCollectorRequest_Content CHECK
    (FencingToken>0 AND ISJSON(COALESCE(PlainContentJson,CONVERT(nvarchar(max),DECOMPRESS(PackedContent))))=1
    AND COALESCE(DATALENGTH(PlainContentJson),DATALENGTH(DECOMPRESS(PackedContent)))<=8388608);
COMMIT TRANSACTION;
GO
