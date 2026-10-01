CREATE TRIGGER [dbo].[TR_AiPortfolioReview_Immutable]
ON [dbo].[AiPortfolioReview]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51815,'AI review evidence is append-only.',1;
END;
