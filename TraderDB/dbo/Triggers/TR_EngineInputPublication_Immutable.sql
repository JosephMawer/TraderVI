CREATE TRIGGER [dbo].[TR_EngineInputPublication_Immutable] ON [dbo].[EngineInputPublication]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54403,'Shared input publications are immutable.',1;
END;
GO
