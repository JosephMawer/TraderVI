CREATE TRIGGER [dbo].[TradingControlEvent_Immutable] ON [dbo].[TradingControlEvent] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51411, 'Trading control history is immutable.', 1;
END;
