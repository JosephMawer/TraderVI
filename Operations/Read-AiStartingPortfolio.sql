-- Run against TraderDB. No persistent writes; @matches is a local table variable.
-- No injected or cloned portfolio is created.
-- These are the existing engine's CURRENT cash/holdings, not a frozen AI input.
-- After reviews run, RecordJson in result 3 contains their saved input/answer evidence.
SET NOCOUNT ON;

DECLARE @matches TABLE (PortfolioId UNIQUEIDENTIFIER);
INSERT INTO @matches (PortfolioId)
SELECT p.PortfolioId
FROM dbo.PaperComparisonAccount a
JOIN dbo.ShadowPortfolio p ON p.PortfolioId = a.ShadowPortfolioId
JOIN dbo.ShadowPortfolioGeneration g ON g.GenerationId = p.GenerationId
WHERE a.StrategyCode = N'BreakoutTop3'
  AND a.GateProfile = N'PortfolioRulesOnlyV1'
  AND p.Status = N'Active' AND g.Status = N'Active';

IF (SELECT COUNT(*) FROM @matches) <> 1
    THROW 51820, 'Expected exactly one active Breakout 3 portfolio with skipped daily gates; inspect the comparison accounts.', 1;

DECLARE @portfolioId UNIQUEIDENTIFIER = (SELECT PortfolioId FROM @matches);

SELECT PortfolioId, DisplayName, CashBalance, UpdatedUtc
FROM dbo.ShadowPortfolio
WHERE PortfolioId = @portfolioId;

SELECT Symbol, Shares, AverageCost, CostBasis, LastPrice, LastPriceEventUtc,
       TrailingStopPrice, UpdatedUtc
FROM dbo.ShadowPosition
WHERE PortfolioId = @portfolioId AND Status = N'Open'
ORDER BY Symbol;

SELECT TOP (20) ReviewId, Status, RecordedUtc, Action, SelectedPortfolioId, RecordJson
FROM dbo.AiPortfolioReview
WHERE AccountScopeId = @portfolioId AND PortfolioId = @portfolioId AND AccountMode = 'paper'
ORDER BY RecordedUtc DESC, ReviewId, Status;
