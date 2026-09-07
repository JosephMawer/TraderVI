CREATE TRIGGER [dbo].[EngineStrategyVersion_Immutable] ON [dbo].[EngineStrategyVersion] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51311, 'Saved strategy versions are immutable. Save a new version.', 1;
END;
