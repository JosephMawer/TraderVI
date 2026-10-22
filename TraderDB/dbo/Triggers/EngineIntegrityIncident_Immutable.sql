CREATE TRIGGER [dbo].[EngineIntegrityIncident_Immutable] ON [dbo].[EngineIntegrityIncident]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54450,'Integrity findings are immutable; use a verified explicit resolution.',1;
END;
GO
