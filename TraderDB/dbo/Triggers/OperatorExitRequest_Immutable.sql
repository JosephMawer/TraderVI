CREATE TRIGGER [dbo].[OperatorExitRequest_Immutable] ON [dbo].[OperatorExitRequest] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51412, 'Operator exit requests are immutable; preserve the request and its execution history.', 1;
END;
