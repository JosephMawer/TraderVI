#nullable enable
namespace Core.Db;

public sealed partial class MarketCollectorRepository
{
    private const string AcquireSql = """
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @now datetime2=SYSUTCDATETIME();
IF EXISTS(SELECT 1 FROM dbo.MarketCollectorLease WITH(UPDLOCK,HOLDLOCK)
 WHERE SourceContract=@Source AND OwnerId<>@Owner AND ExpiresUtc>@now)
BEGIN
 COMMIT TRANSACTION;
 SELECT SourceContract,OwnerId,FencingToken,ExpiresUtc FROM dbo.MarketCollectorLease WHERE 1=0;
 RETURN;
END;
IF NOT EXISTS(SELECT 1 FROM dbo.MarketCollectorLease WITH(UPDLOCK,HOLDLOCK) WHERE SourceContract=@Source)
 INSERT dbo.MarketCollectorLease(SourceContract,OwnerId,FencingToken,ExpiresUtc)
 VALUES(@Source,@Owner,1,DATEADD(SECOND,@Seconds,@now));
ELSE UPDATE dbo.MarketCollectorLease
 SET FencingToken=CASE WHEN ExpiresUtc<=@now OR OwnerId<>@Owner THEN FencingToken+1 ELSE FencingToken END,
 OwnerId=@Owner,ExpiresUtc=DATEADD(SECOND,@Seconds,@now)
 WHERE SourceContract=@Source;
SELECT SourceContract,OwnerId,FencingToken,ExpiresUtc FROM dbo.MarketCollectorLease WHERE SourceContract=@Source;
COMMIT TRANSACTION;
""";
}
