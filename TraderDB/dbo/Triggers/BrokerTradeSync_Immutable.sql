CREATE TRIGGER [dbo].[BrokerTradeSync_Immutable] ON [dbo].[BrokerTradeSync]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51690, 'Broker trade synchronization receipts are immutable. Review corrections explicitly.', 1;
END;
GO
