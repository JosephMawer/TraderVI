CREATE TRIGGER [dbo].[TradingApprovedSettings_Immutable] ON [dbo].[TradingApprovedSettings]
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 54601,'Approved settings are immutable. A replacement requires a new identity.',1;
END;
GO
