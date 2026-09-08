CREATE TRIGGER [dbo].[TradingAccountReport_Immutable] ON [dbo].[TradingAccountReport]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51531,'TradingAccountReport is append-only; preserve the original history.',1;
END;
GO
