CREATE TRIGGER [dbo].[TR_MarketCollectorAttempt_Immutable] ON [dbo].[MarketCollectorAttempt]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54572,'Collector request attempts cannot be erased or retried.',1;
END;
GO
