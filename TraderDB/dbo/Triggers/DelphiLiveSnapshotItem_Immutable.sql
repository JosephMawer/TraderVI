CREATE TRIGGER [dbo].[TR_DelphiLiveSnapshotItem_Immutable] ON [dbo].[DelphiLiveSnapshotItem]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 53705, 'Snapshot item versions are immutable; use a separately reviewed recovery migration.', 1;
END;
GO
