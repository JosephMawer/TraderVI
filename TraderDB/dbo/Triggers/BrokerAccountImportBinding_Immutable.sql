CREATE TRIGGER [dbo].[BrokerAccountImportBinding_Immutable] ON [dbo].[BrokerAccountImportBinding]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51640, 'Broker account import bindings are immutable. Relinking requires a separately reviewed migration.', 1;
END;
GO
