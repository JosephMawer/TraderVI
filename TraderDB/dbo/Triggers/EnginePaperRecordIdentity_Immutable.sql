CREATE TRIGGER [dbo].[TR_EnginePaperRecordIdentity_Immutable] ON [dbo].[EnginePaperRecordIdentity]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54470,'Paper record identities are immutable; use a separately reviewed recovery migration.',1;
END;
GO
