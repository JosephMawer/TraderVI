CREATE TRIGGER [dbo].[TR_EngineConsumerReceipt_Immutable] ON [dbo].[EngineConsumerReceipt]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54436,'Consumer outcomes are immutable.',1;
END;
GO
