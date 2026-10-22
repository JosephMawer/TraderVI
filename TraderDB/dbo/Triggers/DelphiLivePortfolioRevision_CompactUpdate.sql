CREATE TRIGGER [dbo].[TR_DelphiLivePortfolioRevision_CompactUpdate] ON [dbo].[DelphiLivePortfolioRevision]
INSTEAD OF UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted WHERE
        ISNULL(JSON_VALUE(SnapshotJson,'$."$storage".version'),N'') NOT IN(N'2',N'3')
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId')) IS NULL
        OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(SnapshotJson,'$.portfolioId'))<>PortfolioId
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(SnapshotJson,'$.revision'))<>Revision)
        THROW 53707, 'New Live revisions require compact snapshot storage (ADR-0082).', 1;
    -- Validate before replacing the off-row LOB. Reading deleted JSON after a large-to-large
    -- update incurs repeated version-store LOB reads on SQL Server 2019.
    IF EXISTS(SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM deleted
        EXCEPT SELECT PortfolioId,Revision,LeaseId,LeaseFencingToken,SettingsAssignmentId,PersistedUtc FROM inserted)
        OR EXISTS(SELECT 1 FROM deleted WHERE JSON_VALUE(SnapshotJson,'$."$storage".version') IS NOT NULL)
        THROW 53708, 'Existing compact revisions and their provenance are immutable.', 1;
    UPDATE r SET SnapshotJson=i.SnapshotJson
    FROM dbo.DelphiLivePortfolioRevision r
    JOIN inserted i ON i.PortfolioId=r.PortfolioId AND i.Revision=r.Revision;
END;
GO
