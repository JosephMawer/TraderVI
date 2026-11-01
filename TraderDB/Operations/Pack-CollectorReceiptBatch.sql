-- Operator-run after migration 056. Preview with ExecuteBatch=0, MaxBatches=1, VerifiedBackupFile=unused.
-- ExecuteBatch=1 requires a verified backup path and an explicit batch limit (1 to 1000).
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
DECLARE @execute int=TRY_CONVERT(int,N'$(ExecuteBatch)');
IF @execute IS NULL OR @execute NOT IN (0,1) THROW 54643,'Specify ExecuteBatch as 0 (preview) or 1.',1;
DECLARE @limit int=TRY_CONVERT(int,N'$(MaxBatches)');
IF @limit IS NULL OR @limit NOT BETWEEN 1 AND 1000 THROW 54643,'Specify 1 to 1000 bounded batches.',1;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF @execute=1 AND NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
    WHERE b.database_name=DB_NAME() AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
    AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
    THROW 54640,'A fresh verified checksum backup is required for collector receipt conversion.',1;
IF @execute=1 RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
DECLARE @batch TABLE (RequestId uniqueidentifier PRIMARY KEY, Seq bigint NOT NULL, ContentHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    OriginalBytes int NOT NULL, OriginalSha binary(32) NOT NULL, Packed varbinary(max) NOT NULL);
DECLARE @after bigint=0, @count int, @batches int=0, @rows int=0, @oldBytes bigint=0, @newBytes bigint=0;
DECLARE @through bigint=(SELECT MAX(Sequence) FROM dbo.MarketCollectorRequest);
WHILE @batches<@limit
BEGIN
    BEGIN TRANSACTION;
    IF @execute=1 AND EXISTS (SELECT 1 FROM dbo.MarketCollectorLease WITH (TABLOCKX,HOLDLOCK) WHERE ExpiresUtc>SYSUTCDATETIME())
        THROW 54642,'Stop the collector and wait for its writer lease to expire.',1;
    IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id=OBJECT_ID(N'dbo.MarketCollectorRequest')
        AND name=N'TR_MarketCollectorRequest_Immutable' AND is_instead_of_trigger=1 AND is_disabled=0)
        THROW 54641,'Expected the enabled immutable collector receipt guard.',1;
    -- The shortlist fits both a row budget and an 8 MiB original-payload budget.
    ;WITH oldest AS (
        SELECT TOP (64) RequestId,Sequence,DATALENGTH(PlainContentJson) AS Bytes
        FROM dbo.MarketCollectorRequest WHERE Sequence>@after AND Sequence<=@through AND PlainContentJson IS NOT NULL
        ORDER BY Sequence
    ), bounded AS (
        SELECT RequestId,SUM(Bytes) OVER (ORDER BY Sequence ROWS UNBOUNDED PRECEDING) AS TotalBytes FROM oldest
    )
    INSERT @batch
    SELECT r.RequestId,r.Sequence,r.ContentHash,DATALENGTH(r.PlainContentJson),
        HASHBYTES('SHA2_256',CONVERT(varbinary(max),r.PlainContentJson)),COMPRESS(r.PlainContentJson)
    FROM dbo.MarketCollectorRequest r JOIN bounded b ON r.RequestId=b.RequestId WHERE b.TotalBytes<=8388608;
    SET @count=@@ROWCOUNT;
    IF @execute=1 AND @count>0
    BEGIN
        -- The transaction's schema-modification lock lasts through re-enablement and verification.
        -- No other session can write through a temporarily disabled guard.
        DISABLE TRIGGER dbo.TR_MarketCollectorRequest_Immutable ON dbo.MarketCollectorRequest;
        UPDATE r SET PackedContent=b.Packed,PlainContentJson=NULL
        FROM dbo.MarketCollectorRequest r JOIN @batch b ON b.RequestId=r.RequestId
        WHERE r.PackedContent IS NULL AND r.ContentHash=b.ContentHash AND DATALENGTH(r.PlainContentJson)=b.OriginalBytes
          AND CONVERT(char(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),r.PlainContentJson COLLATE Latin1_General_100_BIN2_UTF8)),2)=b.ContentHash
          AND CONVERT(varbinary(max),r.PlainContentJson)=DECOMPRESS(b.Packed);
        IF @@ROWCOUNT<>@count THROW 54644,'Collector receipt changed during conversion.',1;
        ENABLE TRIGGER dbo.TR_MarketCollectorRequest_Immutable ON dbo.MarketCollectorRequest;
        IF EXISTS (SELECT 1 FROM @batch b LEFT JOIN dbo.MarketCollectorRequest r ON r.RequestId=b.RequestId
            WHERE r.RequestId IS NULL OR r.PlainContentJson IS NOT NULL OR r.PackedContent IS NULL
              OR r.ContentHash<>b.ContentHash OR DATALENGTH(r.ContentJson)<>b.OriginalBytes
              OR HASHBYTES('SHA2_256',CONVERT(varbinary(max),r.ContentJson))<>b.OriginalSha)
            THROW 54645,'Collector receipt identity or exact decoded bytes changed.',1;
    END;
    COMMIT TRANSACTION;
    IF @count=0 BREAK;
    SELECT @after=MAX(Seq),@oldBytes+=SUM(CONVERT(bigint,OriginalBytes)),@newBytes+=SUM(DATALENGTH(Packed)) FROM @batch;
    SET @rows+=@count;
    SET @batches+=1;
    DELETE FROM @batch;
END;
SELECT @execute AS ExecuteBatch,@batches AS Batches,@rows AS SelectedRows,@oldBytes AS PlainBytes,@newBytes AS PackedBytes;
GO
