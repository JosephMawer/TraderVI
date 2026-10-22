CREATE TRIGGER [dbo].[DelphiLiveEvaluation_RetiredWriter] ON [dbo].[DelphiLiveEvaluation]
INSTEAD OF INSERT AS
BEGIN
    SET NOCOUNT ON;
    THROW 54453,'The legacy Live writer is retired; use the independent engine input publisher.',1;
END;
GO
