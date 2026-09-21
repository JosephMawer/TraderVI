CREATE TRIGGER [dbo].[BrokerAccountRiskContinuation_Immutable] ON [dbo].[BrokerAccountRiskContinuation]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51670, 'Broker account risk history is immutable. Append verified continuation; never reset or rewrite prior risk references.', 1;
END;
GO
