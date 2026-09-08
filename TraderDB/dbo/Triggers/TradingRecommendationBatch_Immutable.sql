CREATE TRIGGER [dbo].[TradingRecommendationBatch_Immutable] ON [dbo].[TradingRecommendationBatch]
INSTEAD OF UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51533,'TradingRecommendationBatch is append-only; preserve the original history.',1;
END;
GO
