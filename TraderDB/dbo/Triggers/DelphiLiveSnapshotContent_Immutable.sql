CREATE TRIGGER [dbo].[TR_DelphiLiveSnapshotContent_Immutable] ON [dbo].[DelphiLiveSnapshotContent]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 53704, 'Snapshot content is immutable; use a separately reviewed recovery migration.', 1;
END;
GO
