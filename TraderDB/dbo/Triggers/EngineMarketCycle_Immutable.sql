CREATE TRIGGER [dbo].[TR_EngineMarketCycle_Immutable] ON [dbo].[EngineMarketCycle]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54435,'Published market checkpoints are immutable.',1;
END;
GO
