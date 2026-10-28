CREATE TRIGGER [dbo].[TR_MarketCollectorConflict_Immutable] ON [dbo].[MarketCollectorConflict]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54566,'Collector conflicts cannot be silently cleared.',1;
END;
GO
