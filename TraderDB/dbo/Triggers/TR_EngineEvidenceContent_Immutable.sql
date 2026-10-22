CREATE TRIGGER [dbo].[TR_EngineEvidenceContent_Immutable] ON [dbo].[EngineEvidenceContent]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54402,'Shared evidence content is immutable.',1;
END;
GO
