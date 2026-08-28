USE [TraderDB];
GO

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

/*
    Seed the four version-1 calibration outcome definitions used by Athena and
    the read-only WPF Scorecards workspace.

    Expected precondition:
      - dbo.CalibrationCandidate, dbo.CalibrationOutcomeDefinition, and
        dbo.CalibrationCandidateOutcome exist from migration 011.
      - Each expected ID and name/version pair is either absent or already
        matches the canonical contract exactly.

    Data effect:
      - Inserts only missing canonical definition rows.
      - Does not create, update, or delete candidate outcomes, calibration
        evidence, model data, operational trades, or positions.

    Recovery:
      - The operation is additive and transactional. XACT_ABORT rolls back a
        failed transaction. After commit, use a separately authorized
        corrective migration or the verified pre-migration backup; do not
        delete definition rows that may become referenced by outcomes.

    Do not deploy a DACPAC. Review and execute this script manually only after
    a fresh verified backup and explicit authorization.
*/

IF OBJECT_ID(N'dbo.CalibrationCandidate', N'U') IS NULL
    OR OBJECT_ID(N'dbo.CalibrationOutcomeDefinition', N'U') IS NULL
    OR OBJECT_ID(N'dbo.CalibrationCandidateOutcome', N'U') IS NULL
    THROW 51040, 'Calibration outcome tables do not exist. Definition seeding was not started.', 1;

DECLARE @Expected TABLE
(
    [OutcomeDefinitionId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [DefinitionName]      NVARCHAR(64)     NOT NULL,
    [DefinitionVersion]   INT              NOT NULL,
    [DefinitionKind]      NVARCHAR(24)     NOT NULL,
    [DefinitionJson]      NVARCHAR(MAX)    NOT NULL,
    [IsActive]            BIT              NOT NULL,
    UNIQUE ([DefinitionName], [DefinitionVersion])
);

INSERT INTO @Expected
    ([OutcomeDefinitionId], [DefinitionName], [DefinitionVersion], [DefinitionKind], [DefinitionJson], [IsActive])
VALUES
(
    'A72C01CB-9C83-45A6-9A72-CC49E67B9F5A',
    N'PredictionLabels10',
    1,
    N'Prediction',
    N'{"schemaVersion":1,"horizonSessions":10,"labelSource":"ProfitModelRegistry.ILabeler","benchmark":"XIU"}',
    1
),
(
    'FA0C8F51-0C48-4E0C-BB26-DFBD82C0D640',
    N'PredictionPath20',
    1,
    N'Prediction',
    N'{"schemaVersion":1,"horizons":[1,5,10,20],"start":"observationClose","benchmark":"XIU"}',
    1
),
(
    '491D7C6C-EBBB-4B5E-8259-3E3169D732B6',
    N'SwingMarkToMarket3',
    1,
    N'Tradeable',
    N'{"schemaVersion":1,"measure":"markToMarket","horizons":[1,2,3],"population":"publishedLensCandidates","entry":"firstEligibleOpen","entryTimeZone":"America/Toronto","marketOpenLocal":"09:30:00","entrySessionAllowance":3,"slippageRatePerSide":0.001,"halfSpreadRatePerSide":0.0015,"benchmark":"XIU","benchmarkCosts":false}',
    1
),
(
    'BBB218C1-616E-46F5-A70B-826E547A7DE3',
    N'SwingExcursion3',
    1,
    N'Tradeable',
    N'{"schemaVersion":1,"measure":"excursion","horizons":[1,2,3],"population":"publishedLensCandidates","entry":"firstEligibleOpen","entryTimeZone":"America/Toronto","marketOpenLocal":"09:30:00","entrySessionAllowance":3,"mfe":"maxHigh/rawEntry-1","mae":"minLow/rawEntry-1","maeSign":"nonPositive","timeUnit":"sessionOrdinal","ties":"earliestSession","sameSessionOrder":"unknown","costAdjusted":false}',
    1
);

DECLARE @CandidateRowsBefore BIGINT = (SELECT COUNT_BIG(*) FROM [dbo].[CalibrationCandidate]);
DECLARE @OutcomeRowsBefore BIGINT = (SELECT COUNT_BIG(*) FROM [dbo].[CalibrationCandidateOutcome]);

SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

IF EXISTS
(
    SELECT 1
    FROM @Expected AS e
    JOIN [dbo].[CalibrationOutcomeDefinition] AS d WITH (UPDLOCK, HOLDLOCK)
      ON d.[OutcomeDefinitionId] = e.[OutcomeDefinitionId]
    WHERE d.[DefinitionName] <> e.[DefinitionName]
       OR d.[DefinitionVersion] <> e.[DefinitionVersion]
       OR d.[DefinitionKind] <> e.[DefinitionKind]
       OR d.[DefinitionJson] <> e.[DefinitionJson]
       OR d.[IsActive] <> e.[IsActive]
)
    THROW 51041, 'An expected outcome-definition ID exists with a conflicting contract. Migration refused.', 1;

IF EXISTS
(
    SELECT 1
    FROM @Expected AS e
    JOIN [dbo].[CalibrationOutcomeDefinition] AS d WITH (UPDLOCK, HOLDLOCK)
      ON d.[DefinitionName] = e.[DefinitionName]
     AND d.[DefinitionVersion] = e.[DefinitionVersion]
    WHERE d.[OutcomeDefinitionId] <> e.[OutcomeDefinitionId]
)
    THROW 51042, 'An expected outcome-definition name/version exists under a conflicting ID. Migration refused.', 1;

INSERT INTO [dbo].[CalibrationOutcomeDefinition]
    ([OutcomeDefinitionId], [DefinitionName], [DefinitionVersion], [DefinitionKind], [DefinitionJson], [IsActive])
SELECT
    e.[OutcomeDefinitionId],
    e.[DefinitionName],
    e.[DefinitionVersion],
    e.[DefinitionKind],
    e.[DefinitionJson],
    e.[IsActive]
FROM @Expected AS e
WHERE NOT EXISTS
(
    SELECT 1
    FROM [dbo].[CalibrationOutcomeDefinition] AS d WITH (UPDLOCK, HOLDLOCK)
    WHERE d.[OutcomeDefinitionId] = e.[OutcomeDefinitionId]
);

IF
(
    SELECT COUNT_BIG(*)
    FROM @Expected AS e
    JOIN [dbo].[CalibrationOutcomeDefinition] AS d
      ON d.[OutcomeDefinitionId] = e.[OutcomeDefinitionId]
     AND d.[DefinitionName] = e.[DefinitionName]
     AND d.[DefinitionVersion] = e.[DefinitionVersion]
     AND d.[DefinitionKind] = e.[DefinitionKind]
     AND d.[DefinitionJson] = e.[DefinitionJson]
     AND d.[IsActive] = e.[IsActive]
) <> 4
    THROW 51043, 'Canonical outcome-definition verification failed. Transaction will be rolled back.', 1;

IF (SELECT COUNT_BIG(*) FROM [dbo].[CalibrationCandidate]) <> @CandidateRowsBefore
    OR (SELECT COUNT_BIG(*) FROM [dbo].[CalibrationCandidateOutcome]) <> @OutcomeRowsBefore
    THROW 51044, 'Calibration evidence row counts changed unexpectedly. Transaction will be rolled back.', 1;

COMMIT TRANSACTION;

SELECT
    d.[OutcomeDefinitionId],
    d.[DefinitionName],
    d.[DefinitionVersion],
    d.[DefinitionKind],
    d.[IsActive]
FROM [dbo].[CalibrationOutcomeDefinition] AS d
JOIN @Expected AS e ON e.[OutcomeDefinitionId] = d.[OutcomeDefinitionId]
ORDER BY d.[DefinitionName], d.[DefinitionVersion];

SELECT
    [CandidateRows] = COUNT_BIG(*),
    [OutcomeRows] = (SELECT COUNT_BIG(*) FROM [dbo].[CalibrationCandidateOutcome])
FROM [dbo].[CalibrationCandidate];
