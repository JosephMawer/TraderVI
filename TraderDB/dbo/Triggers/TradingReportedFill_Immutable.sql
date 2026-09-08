CREATE TRIGGER [dbo].[TradingReportedFill_Immutable] ON [dbo].[TradingReportedFill]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51534,'TradingReportedFill is append-only; preserve the original history.',1;
END;
GO
