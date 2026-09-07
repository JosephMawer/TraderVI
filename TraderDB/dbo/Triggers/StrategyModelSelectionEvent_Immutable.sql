CREATE TRIGGER [dbo].[StrategyModelSelectionEvent_Immutable] ON [dbo].[StrategyModelSelectionEvent]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51261, 'Strategy model selection history is append-only.', 1;
END;
