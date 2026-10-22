CREATE TRIGGER [dbo].[DelphiLiveEvaluation_ArchiveImmutable] ON [dbo].[DelphiLiveEvaluation]
INSTEAD OF UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 54454,'The legacy Live evaluation archive is immutable.',1;
END;
GO
