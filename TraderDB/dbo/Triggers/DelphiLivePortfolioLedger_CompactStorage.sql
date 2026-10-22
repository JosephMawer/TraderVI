CREATE TRIGGER [dbo].[TR_DelphiLivePortfolioLedger_CompactStorage] ON [dbo].[DelphiLivePortfolioLedger]
AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'') NOT IN(N'2',N'3')
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId')) IS NULL
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId'))<>PortfolioId
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision'))<>Revision)
        THROW 53706, 'This Live writer must be upgraded to compact snapshot storage (ADR-0082).', 1;
END;
GO
