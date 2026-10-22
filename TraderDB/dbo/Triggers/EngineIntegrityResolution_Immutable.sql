CREATE TRIGGER [dbo].[EngineIntegrityResolution_Immutable] ON [dbo].[EngineIntegrityResolution]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54451,'Integrity resolutions are immutable.',1;
END;
GO
