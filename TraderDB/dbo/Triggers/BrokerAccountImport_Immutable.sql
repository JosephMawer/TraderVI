CREATE TRIGGER [dbo].[BrokerAccountImport_Immutable] ON [dbo].[BrokerAccountImport]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51641, 'Broker account import observations are immutable. Append a reviewed observation; preserve existing history.', 1;
END;
GO
