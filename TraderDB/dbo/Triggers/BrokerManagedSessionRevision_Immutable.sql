CREATE TRIGGER [dbo].[BrokerManagedSessionRevision_Immutable] ON [dbo].[BrokerManagedSessionRevision]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 54003, 'Broker strategy sessions are immutable. Append a reviewed state; never remove order intent or recovery history.', 1;
END;
GO
