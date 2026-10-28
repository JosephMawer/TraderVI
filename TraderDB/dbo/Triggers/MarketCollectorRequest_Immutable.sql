CREATE TRIGGER [dbo].[TR_MarketCollectorRequest_Immutable] ON [dbo].[MarketCollectorRequest]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54564,'Collector source receipts are immutable.',1;
END;
GO
