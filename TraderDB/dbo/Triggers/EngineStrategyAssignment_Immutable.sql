CREATE TRIGGER [dbo].[EngineStrategyAssignment_Immutable] ON [dbo].[EngineStrategyAssignment] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51312, 'Strategy assignment history is immutable.', 1;
END;
