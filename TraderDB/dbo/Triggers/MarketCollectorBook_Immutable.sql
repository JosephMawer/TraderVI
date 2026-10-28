CREATE TRIGGER [dbo].[TR_MarketCollectorBook_Immutable] ON [dbo].[MarketCollectorBook]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54563,'Collector session identity is immutable.',1;
END;
GO
