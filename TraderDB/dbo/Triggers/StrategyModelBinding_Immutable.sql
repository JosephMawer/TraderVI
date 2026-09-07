CREATE TRIGGER [dbo].[StrategyModelBinding_Immutable] ON [dbo].[StrategyModelBinding]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51260, 'Strategy model assignments are immutable; register a new strategy version.', 1;
END;
