CREATE TRIGGER [dbo].[TR_DelphiLivePortfolioRevision_CompactStorage] ON [dbo].[DelphiLivePortfolioRevision]
AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'')<>N'2'
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId')) IS NULL
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId'))<>PortfolioId
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision'))<>Revision)
        THROW 53707, 'New Live revisions require compact snapshot storage (ADR-0082).', 1;
    -- The reviewed maintenance tool may replace legacy JSON only; identity/time/fences never change.
    IF EXISTS(SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM deleted
        EXCEPT SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM inserted)
        OR EXISTS(SELECT 1 FROM deleted WHERE JSON_VALUE(SnapshotJson,'$."$storage".version') IS NOT NULL)
        THROW 53708, 'Existing compact revisions and their provenance are immutable.', 1;
END;
GO
