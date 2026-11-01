-- Prepared only. Apply after 054 and before compact storage activation.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Required session settings for filtered indexes and their later DML.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54604,'A fresh checksum full backup is required for compact storage installation.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.TradingOrder',N'U') IS NULL
    THROW 54607,'Install the compact order schema first.',1;
IF EXISTS (SELECT 1 FROM dbo.TradingOrder WITH (TABLOCKX,HOLDLOCK))
    THROW 54608,'This pre-activation migration requires empty compact order storage.',1;
IF (SELECT COUNT(*) FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.TradingOrder') AND is_disabled=0 AND is_not_trusted=0
    AND name IN (N'CK_TradingOrder_2',N'CK_TradingOrder_7') AND definition NOT LIKE N'%Expired%')<>2
    OR NOT EXISTS (SELECT 1 FROM sys.triggers WHERE object_id=OBJECT_ID(N'dbo.TradingOrder_IntentImmutable')
        AND parent_id=OBJECT_ID(N'dbo.TradingOrder') AND is_disabled=0)
    THROW 54609,'Expected the original enabled 054 order guards.',1;
ALTER TABLE dbo.TradingOrder DROP CONSTRAINT [CK_TradingOrder_2], [CK_TradingOrder_7];
ALTER TABLE dbo.TradingOrder WITH CHECK ADD CONSTRAINT [CK_TradingOrder_2] CHECK ([Status] IN (N'Prepared',N'Submitted',N'Unknown',N'Filled',N'Rejected',N'Cancelled',N'Expired'));
ALTER TABLE dbo.TradingOrder WITH CHECK ADD CONSTRAINT [CK_TradingOrder_7] CHECK ([NeedsWork]=1 OR ([Status] IN (N'Filled',N'Rejected',N'Cancelled',N'Expired') AND [AppliedFilledQuantity]=[LatestFilledQuantity] AND [AppliedNetValue]=[LatestNetValue] AND [ReservedQuantity]=0 AND [ReservedCash]=0));
GO
ALTER TRIGGER [dbo].[TradingOrder_IntentImmutable] ON [dbo].[TradingOrder]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT BindingId,RequestId,DecisionId,PermissionId,PositionId,SecurityId,Symbol,Side,Quantity,ReferencePrice,CreatedUtc FROM deleted
        EXCEPT
        SELECT BindingId,RequestId,DecisionId,PermissionId,PositionId,SecurityId,Symbol,Side,Quantity,ReferencePrice,CreatedUtc FROM inserted)
        THROW 54602,'Original order intent cannot change.',1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.BindingId=d.BindingId AND i.RequestId=d.RequestId
        WHERE (d.BrokerOrderId IS NOT NULL AND (i.BrokerOrderId IS NULL OR d.BrokerOrderId<>i.BrokerOrderId))
        OR i.LatestFilledQuantity<d.LatestFilledQuantity OR i.AppliedFilledQuantity<d.AppliedFilledQuantity
        OR i.LatestNetValue<d.LatestNetValue OR i.AppliedNetValue<d.AppliedNetValue
        OR (d.Status IN (N'Filled',N'Rejected',N'Cancelled',N'Expired') AND d.Status<>i.Status)
        OR (d.NeedsWork=0 AND (i.NeedsWork=1 OR i.LatestFilledQuantity<>d.LatestFilledQuantity
            OR i.AppliedFilledQuantity<>d.AppliedFilledQuantity OR i.LatestNetValue<>d.LatestNetValue
            OR i.AppliedNetValue<>d.AppliedNetValue OR i.ReservedQuantity<>d.ReservedQuantity OR i.ReservedCash<>d.ReservedCash)))
        THROW 54603,'Order outcome regressed or completed accounting was reopened.',1;
END;
GO

COMMIT TRANSACTION;
GO
