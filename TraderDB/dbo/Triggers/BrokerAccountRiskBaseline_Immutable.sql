CREATE TRIGGER [dbo].[BrokerAccountRiskBaseline_Immutable] ON [dbo].[BrokerAccountRiskBaseline]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 THROW 51650, 'Initial broker risk baselines are immutable. Preserve setup evidence; subsequent risk events require their own reviewed workflow.', 1;
END;
GO
