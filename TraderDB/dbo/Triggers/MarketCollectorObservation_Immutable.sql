CREATE TRIGGER [dbo].[TR_MarketCollectorObservation_Immutable] ON [dbo].[MarketCollectorObservation]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54565,'First completed collector observations are immutable.',1;
END;
GO
