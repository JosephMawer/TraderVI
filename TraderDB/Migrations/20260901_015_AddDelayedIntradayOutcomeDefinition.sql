USE [TraderDB];
GO

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

/*
    Add the version-1 delayed-intraday swing outcome contract accepted by
    ADR-0040. This migration defines the calculation only; it does not create
    candidate outcomes or touch operational Ghost/Real trades.

    Review and execute manually only after a fresh verified backup and explicit
    authorization. Do not deploy a DACPAC.
*/

IF OBJECT_ID(N'dbo.CalibrationOutcomeDefinition', N'U') IS NULL
    OR OBJECT_ID(N'dbo.CalibrationCandidateOutcome', N'U') IS NULL
    THROW 51050, 'Calibration outcome tables do not exist. Migration was not started.', 1;

DECLARE @OutcomeDefinitionId UNIQUEIDENTIFIER = '77134C9C-595A-4BF4-9DB7-2AE67FA48C92';
DECLARE @DefinitionName NVARCHAR(64) = N'DelayedIntradaySwing';
DECLARE @DefinitionVersion INT = 1;
DECLARE @DefinitionKind NVARCHAR(24) = N'Tradeable';
DECLARE @DefinitionJson NVARCHAR(MAX) =
    N'{"schemaVersion":1,"measure":"policyExit","population":"publishedLensCandidates","entry":"firstEligibleOpen","entrySessionAllowance":3,"policyBarMinutes":15,"fill":"firstFiveMinuteBarOpenAtOrAfterDetection","grossCommissionRate":0.0,"executionFrictionRatePerSide":0.0025,"benchmark":"XIU","benchmarkAlignment":"sameFiveMinuteBarStart"}';

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

IF EXISTS
(
    SELECT 1
    FROM [dbo].[CalibrationOutcomeDefinition] WITH (UPDLOCK, HOLDLOCK)
    WHERE [OutcomeDefinitionId] = @OutcomeDefinitionId
      AND ([DefinitionName] <> @DefinitionName
       OR [DefinitionVersion] <> @DefinitionVersion
       OR [DefinitionKind] <> @DefinitionKind
       OR [DefinitionJson] <> @DefinitionJson
       OR [IsActive] <> 1)
)
    THROW 51051, 'The delayed-intraday definition ID conflicts with the canonical contract.', 1;

IF EXISTS
(
    SELECT 1
    FROM [dbo].[CalibrationOutcomeDefinition] WITH (UPDLOCK, HOLDLOCK)
    WHERE [DefinitionName] = @DefinitionName
      AND [DefinitionVersion] = @DefinitionVersion
      AND [OutcomeDefinitionId] <> @OutcomeDefinitionId
)
    THROW 51052, 'The delayed-intraday name/version exists under another ID.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM [dbo].[CalibrationOutcomeDefinition] WITH (UPDLOCK, HOLDLOCK)
    WHERE [OutcomeDefinitionId] = @OutcomeDefinitionId
)
BEGIN
    INSERT INTO [dbo].[CalibrationOutcomeDefinition]
        ([OutcomeDefinitionId],[DefinitionName],[DefinitionVersion],[DefinitionKind],[DefinitionJson],[IsActive])
    VALUES
        (@OutcomeDefinitionId,@DefinitionName,@DefinitionVersion,@DefinitionKind,@DefinitionJson,1);
END;

COMMIT TRANSACTION;

SELECT [OutcomeDefinitionId],[DefinitionName],[DefinitionVersion],[DefinitionKind],[DefinitionJson],[IsActive]
FROM [dbo].[CalibrationOutcomeDefinition]
WHERE [OutcomeDefinitionId] = @OutcomeDefinitionId;
