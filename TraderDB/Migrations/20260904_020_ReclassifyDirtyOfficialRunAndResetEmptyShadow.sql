/*
Purpose: Apply the operator-authorized one-time correction for the 2026-09-04
         official Delphi run and remove the empty Shadow generation that was
         created before that run was considered valid.
Data effect: Changes one CalibrationRun from Degraded to Valid while preserving
             its Dirty working-tree provenance, then deletes one precisely
             identified empty Shadow generation and its startup audit rows.
Safety: Requires a fresh verified backup. Refuses to run unless the Delphi run
        and every Shadow row still match the reviewed preflight state, including
        zero candidates, positions, and orders.
*/

USE [TraderDB];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @RunId uniqueidentifier = '463416D8-8229-4C51-B255-107F96DF21D4';
DECLARE @GenerationId uniqueidentifier = '246C3C70-37B8-4CB7-AD24-F044FAF16477';

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.CalibrationRun WITH (UPDLOCK, HOLDLOCK)
        WHERE RunId = @RunId
          AND RunPurpose = N'OfficialPaper'
          AND RecommendationDate = CONVERT(date, '2026-09-04')
          AND MarketDataAsOf = CONVERT(date, '2026-09-03')
          AND AuditState = N'Degraded'
          AND AuditMessage = N'Working tree state is Dirty.'
          AND WorkingTreeState = N'Dirty'
          AND CodeCommit = N'f3e92dd4b7e44430fbdb6b15c523f56ec5c1ee52'
          AND StrategyVersionId = '2BD1A7D0-D144-4A7B-9FA4-49606AB7E963'
    )
        THROW 51000, 'The target Delphi run no longer matches the reviewed degraded row.', 1;

    IF (SELECT COUNT(*) FROM dbo.CalibrationCandidate WHERE RunId = @RunId) <> 211
        THROW 51001, 'The target Delphi candidate count is no longer 211.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.CalibrationLensEvaluation AS lens
        INNER JOIN dbo.CalibrationCandidate AS candidate
            ON candidate.CandidateId = lens.CandidateId
        WHERE candidate.RunId = @RunId
    ) <> 422
        THROW 51002, 'The target Delphi lens-evaluation count is no longer 422.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.CalibrationLensEvaluation AS lens
        INNER JOIN dbo.CalibrationCandidate AS candidate
            ON candidate.CandidateId = lens.CandidateId
        WHERE candidate.RunId = @RunId
          AND lens.IsPublished = 1
    ) <> 50
        THROW 51003, 'The target Delphi published-pick count is no longer 50.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.ShadowPortfolioGeneration WITH (UPDLOCK, HOLDLOCK)
        WHERE GenerationId = @GenerationId
          AND PolicyVersion = N'SystemShadowV1'
          AND Status = N'Active'
          AND TotalAccountValue = CONVERT(decimal(19, 6), 749.50)
          AND AvailableAccountCash = CONVERT(decimal(19, 6), 749.50)
          AND ActivatedUtc = CONVERT(datetime2, '2026-09-04T14:30:09.9548258')
          AND StoppedUtc IS NULL
    )
        THROW 51004, 'The target Shadow generation no longer matches the reviewed active generation.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.ShadowPortfolio
        WHERE GenerationId = @GenerationId
          AND Status = N'Active'
          AND CashBalance = CONVERT(decimal(19, 6), 749.50)
          AND HighestClosingValue = CONVERT(decimal(19, 6), 749.50)
          AND PortfolioCode IN
              (N'ContinuationTop3', N'ContinuationTop5', N'BreakoutTop3', N'BreakoutTop5')
    ) <> 4
        THROW 51005, 'The target Shadow generation no longer has the four reviewed empty portfolios.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.ShadowPortfolioSession AS session
        INNER JOIN dbo.ShadowPortfolio AS portfolio
            ON portfolio.PortfolioId = session.PortfolioId
        WHERE portfolio.GenerationId = @GenerationId
          AND session.TradingDate = CONVERT(date, '2026-09-04')
          AND session.Status = N'NoValidDelphiRun'
          AND session.CalibrationRunId IS NULL
          AND session.ActivationBaselineUtc = CONVERT(datetime2, '2026-09-04T14:30:09.9548258')
    ) <> 4
        THROW 51006, 'The target Shadow generation no longer has exactly four reviewed no-run sessions.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.ShadowPortfolioCandidate AS candidate
        INNER JOIN dbo.ShadowPortfolioSession AS session
            ON session.SessionId = candidate.SessionId
        INNER JOIN dbo.ShadowPortfolio AS portfolio
            ON portfolio.PortfolioId = session.PortfolioId
        WHERE portfolio.GenerationId = @GenerationId
    )
        THROW 51007, 'The target Shadow generation now contains candidates and will not be reset.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.ShadowPosition AS position
        INNER JOIN dbo.ShadowPortfolio AS portfolio
            ON portfolio.PortfolioId = position.PortfolioId
        WHERE portfolio.GenerationId = @GenerationId
    )
        THROW 51008, 'The target Shadow generation now contains positions and will not be reset.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.ShadowOrder AS shadowOrder
        INNER JOIN dbo.ShadowPortfolio AS portfolio
            ON portfolio.PortfolioId = shadowOrder.PortfolioId
        WHERE portfolio.GenerationId = @GenerationId
    )
        THROW 51009, 'The target Shadow generation now contains orders and will not be reset.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.ShadowPortfolioEvent AS shadowEvent
        INNER JOIN dbo.ShadowPortfolio AS portfolio
            ON portfolio.PortfolioId = shadowEvent.PortfolioId
        WHERE portfolio.GenerationId = @GenerationId
          AND shadowEvent.EventType = N'Lifecycle'
          AND shadowEvent.ReasonCode IN (N'Activated', N'NoValidDelphiRun')
    ) <> 8
        THROW 51010, 'The target Shadow generation no longer has exactly eight reviewed startup events.', 1;

    IF
    (
        SELECT COUNT(*)
        FROM dbo.ShadowCapitalEvent
        WHERE GenerationId = @GenerationId
          AND EventType = N'InitialSnapshot'
          AND TotalAccountValue = CONVERT(decimal(19, 6), 749.50)
          AND AvailableAccountCash = CONVERT(decimal(19, 6), 749.50)
          AND ExternalFlowAmount IS NULL
    ) <> 1
        THROW 51011, 'The target Shadow generation no longer has exactly one reviewed initial capital event.', 1;

    UPDATE dbo.CalibrationRun
    SET AuditState = N'Valid',
        AuditMessage = NULL
    WHERE RunId = @RunId
      AND AuditState = N'Degraded'
      AND AuditMessage = N'Working tree state is Dirty.'
      AND WorkingTreeState = N'Dirty';

    IF @@ROWCOUNT <> 1
        THROW 51012, 'The Delphi run update did not affect exactly one row.', 1;

    DELETE shadowEvent
    FROM dbo.ShadowPortfolioEvent AS shadowEvent
    INNER JOIN dbo.ShadowPortfolio AS portfolio
        ON portfolio.PortfolioId = shadowEvent.PortfolioId
    WHERE portfolio.GenerationId = @GenerationId;

    IF @@ROWCOUNT <> 8
        THROW 51013, 'The Shadow event reset did not remove exactly eight rows.', 1;

    DELETE session
    FROM dbo.ShadowPortfolioSession AS session
    INNER JOIN dbo.ShadowPortfolio AS portfolio
        ON portfolio.PortfolioId = session.PortfolioId
    WHERE portfolio.GenerationId = @GenerationId;

    IF @@ROWCOUNT <> 4
        THROW 51014, 'The Shadow session reset did not remove exactly four rows.', 1;

    DELETE FROM dbo.ShadowCapitalEvent
    WHERE GenerationId = @GenerationId;

    IF @@ROWCOUNT <> 1
        THROW 51015, 'The Shadow capital reset did not remove exactly one row.', 1;

    DELETE FROM dbo.ShadowPortfolio
    WHERE GenerationId = @GenerationId;

    IF @@ROWCOUNT <> 4
        THROW 51016, 'The Shadow portfolio reset did not remove exactly four rows.', 1;

    DELETE FROM dbo.ShadowPortfolioGeneration
    WHERE GenerationId = @GenerationId;

    IF @@ROWCOUNT <> 1
        THROW 51017, 'The Shadow generation reset did not remove exactly one row.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    RunId,
    AuditState,
    AuditMessage,
    WorkingTreeState
FROM dbo.CalibrationRun
WHERE RunId = @RunId;

SELECT
    (SELECT COUNT(*) FROM dbo.ShadowPortfolioGeneration WHERE GenerationId = @GenerationId) AS GenerationRows,
    (SELECT COUNT(*) FROM dbo.ShadowPortfolio WHERE GenerationId = @GenerationId) AS PortfolioRows;
GO
