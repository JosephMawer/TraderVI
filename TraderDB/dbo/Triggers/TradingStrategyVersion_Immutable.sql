CREATE TRIGGER [dbo].[TradingStrategyVersion_Immutable] ON [dbo].[TradingStrategyVersion]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51530,'TradingStrategyVersion is append-only; preserve the original history.',1;
END;
GO
