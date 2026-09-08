CREATE TRIGGER [dbo].[TradingPromotion_Immutable] ON [dbo].[TradingPromotion]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51532,'TradingPromotion is append-only; preserve the original history.',1;
END;
GO
