:ON ERROR EXIT

USE [TraderDB];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

/*
    Add the inactive Delphi Live V1 identity and shared five-minute collection
    foundation accepted by ADR-0053.

    Expected preconditions:
      - Migrations through 021 are applied.
      - Strategy, immutable calibration, symbol, and intraday evidence ledgers
        exist.
      - All fourteen new tables are absent. A partial or prior installation
        must be reviewed instead of silently repaired.

    Data effects:
      - Creates identity, assignment, durable host-lease/continuity, frozen
        session-source, baseline, scheduled collection-slot, and conflict
        provenance tables.
      - Seeds exactly one immutable DelphiLivePolicyV1 definition in the
        Inactive installation state.
      - Creates no assignment, lease, capital, portfolio, session, candidate,
        baseline, collection, or conflict rows. Nothing is activated.

    Recovery:
      - The DDL and one inactive definition insert are one SERIALIZABLE
        transaction with XACT_ABORT.
      - Restore the fresh verified pre-migration backup if an authorized
        rollback is required after commit. Do not edit this migration.

    Review and execute from the repository root with SQLCMD only after a fresh
    verified backup and explicit authorization. Do not deploy a DACPAC.
*/

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (OBJECT_ID(N'dbo.StrategyVersion', N'U')),
            (OBJECT_ID(N'dbo.CalibrationRun', N'U')),
            (OBJECT_ID(N'dbo.CalibrationCandidate', N'U')),
            (OBJECT_ID(N'dbo.CalibrationLensEvaluation', N'U')),
            (OBJECT_ID(N'dbo.Symbols', N'U')),
            (OBJECT_ID(N'dbo.IntradayPollObservation', N'U')),
            (OBJECT_ID(N'dbo.IntradayEvidenceBar', N'U'))
    ) AS required ([object_id])
    WHERE required.[object_id] IS NULL
)
    THROW 51220, 'Required strategy, calibration, symbol, or intraday evidence ledger is missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (OBJECT_ID(N'dbo.DelphiLivePolicyVersion', N'U')),
            (OBJECT_ID(N'dbo.DelphiLivePolicyAssignment', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveHostLease', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveSession', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveSessionPolicy', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveContinuityEpoch', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveSessionSymbol', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveFrozenCandidate', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveFrozenCandidateLens', N'U')),
            (OBJECT_ID(N'dbo.DelphiLiveDailyBaseline', N'U')),
            (OBJECT_ID(N'dbo.IntradayCollectionCycle', N'U')),
            (OBJECT_ID(N'dbo.IntradayCollectionSlot', N'U')),
            (OBJECT_ID(N'dbo.IntradayEvidenceConflict', N'U')),
            (OBJECT_ID(N'dbo.IntradayCollectionReceipt', N'U'))
    ) AS proposed ([object_id])
    WHERE proposed.[object_id] IS NOT NULL
)
    THROW 51221, 'A Delphi Live foundation table already exists; review the partial or prior installation.', 1;

CREATE TABLE #PreMigrationCounts
(
    [ObjectName] SYSNAME NOT NULL PRIMARY KEY,
    [RowCount]   BIGINT  NOT NULL
);

INSERT INTO #PreMigrationCounts ([ObjectName], [RowCount])
SELECT N'StrategyVersion', COUNT_BIG(*) FROM dbo.StrategyVersion
UNION ALL SELECT N'CalibrationRun', COUNT_BIG(*) FROM dbo.CalibrationRun
UNION ALL SELECT N'CalibrationCandidate', COUNT_BIG(*) FROM dbo.CalibrationCandidate
UNION ALL SELECT N'CalibrationLensEvaluation', COUNT_BIG(*) FROM dbo.CalibrationLensEvaluation
UNION ALL SELECT N'IntradayPollObservation', COUNT_BIG(*) FROM dbo.IntradayPollObservation
UNION ALL SELECT N'IntradayEvidenceBar', COUNT_BIG(*) FROM dbo.IntradayEvidenceBar;
GO

BEGIN TRANSACTION;
GO

:r TraderDB\dbo\Tables\DelphiLivePolicyVersion.sql
:r TraderDB\dbo\Tables\DelphiLivePolicyAssignment.sql
:r TraderDB\dbo\Tables\DelphiLiveHostLease.sql
:r TraderDB\dbo\Tables\DelphiLiveSession.sql
:r TraderDB\dbo\Tables\DelphiLiveSessionPolicy.sql
:r TraderDB\dbo\Tables\DelphiLiveContinuityEpoch.sql
:r TraderDB\dbo\Tables\DelphiLiveSessionSymbol.sql
:r TraderDB\dbo\Tables\DelphiLiveFrozenCandidate.sql
:r TraderDB\dbo\Tables\DelphiLiveFrozenCandidateLens.sql
:r TraderDB\dbo\Tables\DelphiLiveDailyBaseline.sql
:r TraderDB\dbo\Tables\IntradayCollectionCycle.sql
:r TraderDB\dbo\Tables\IntradayCollectionSlot.sql
:r TraderDB\dbo\Tables\IntradayEvidenceConflict.sql
:r TraderDB\dbo\Tables\IntradayCollectionReceipt.sql

DECLARE @PolicyVersionId UNIQUEIDENTIFIER = 'C15C1A27-13A1-581A-8912-06C92941A01E';
DECLARE @SettingsJson NVARCHAR(MAX) = N'{"marketTimeZone":"America/Toronto","barInterval":"00:05:00","collectionOffset":"00:02:00","persistenceObservationCount":4,"immediateMovementHorizon":"00:20:00","sustainedMovementHorizon":"01:00:00","twoHourContextHorizon":"02:00:00","threeHourContextHorizon":"03:00:00","directionalVolumeObservationCount":4,"priorRangeObservationCount":4,"minimumStructureReferences":2,"volatilityRulers":{"diagnosticShortSessions":5,"operationalSessions":10,"challengerSessions":14,"diagnosticLongSessions":20},"rawMoveThresholds":{"lower":0.15,"operational":0.25,"upper":0.35},"excessMoveThresholds":{"lower":0.025,"operational":0.05,"upper":0.1},"selectedRawMoveThreshold":0.25,"selectedExcessMoveThreshold":0.05,"selectedRulerSessions":10,"directionalVolumeThreshold":0.1,"structureBufferUnits":0.05,"fullDayVolumeMedianSessionCount":20,"entryConfirmationCount":2,"weakeningConfirmationCount":2,"hardLossFraction":0.05,"fastDownsideReturnFloor":-0.1,"profitFloorActivationGainFraction":0.03,"trailingActivationGainFraction":0.05,"trailingDistanceFraction":0.02,"maximumHoldings":5,"entryTargetNavFraction":0.2,"maximumSameSessionEntriesPerSymbol":2,"dailyLossGuardFraction":0.03,"capitalReviewDrawdownFraction":0.1,"quoteAttemptCount":3,"quoteAttemptWindow":"00:01:00","entryWindowStart":"09:50:00","entryCutoff":"15:45:00","primaryExitReasonOrder":["HardLoss5Pct","FastDownside10Pct","ProfitProtectionFloorBreach","ConfirmedSupportFailure","LiveWeakeningExit"],"opportunityThresholds":[0.01,0.02,0.03,0.05,0.1,0.15],"researchSessionHorizons":[1,3,5],"engineeringShakedownSessionCount":10,"discoverySessionCount":30,"untouchedConfirmationSessionCount":30,"promotionBootstrapResampleCount":10000,"promotionBootstrapBlockSessionCount":5,"promotionConfidenceLevel":0.95,"degradedCoverageFloor":0.95,"readyCoverage":1,"maximumActiveNonChampionPolicies":2}';
DECLARE @SettingsSha256 BINARY(32) = 0xA1944AC94212353A43D8291D1A6B9E3ACAB992F77E69FCFE559A814AEE2FDA99;

INSERT INTO [dbo].[DelphiLivePolicyVersion]
(
    [DelphiLivePolicyVersionId],
    [PolicyDefinitionName],
    [PolicyDefinitionSchemaVersion],
    [EvaluatorVersion],
    [CollectorVersion],
    [CollectorSourceContractVersion],
    [DecisionDossierVersion],
    [DecisionDossierSchemaVersion],
    [QuoteFillVersion],
    [ShadowPortfolioVersion],
    [ResearchOutcomeVersion],
    [RankingDiagnosticVersion],
    [PromotionProtocolVersion],
    [SettingsJson],
    [SettingsEncoding],
    [SettingsSha256],
    [InitialActivationState],
    [DecisionRef]
)
VALUES
(
    @PolicyVersionId,
    N'DelphiLivePolicyV1',
    1,
    N'DelphiLiveEvaluatorV1',
    N'IntradayEvidenceCollectorV3',
    1,
    N'DelphiLiveDecisionDossierV1',
    1,
    N'DelphiLiveQuoteFillV1',
    N'DelphiLiveShadowPortfolioV1',
    N'LiveObservationOutcomeV1',
    N'DelphiLiveDailyVsLiveTop5V1',
    N'DelphiLivePromotionV1',
    @SettingsJson,
    N'UTF-8',
    @SettingsSha256,
    N'Inactive',
    N'ADR-0053'
);

IF
(
    SELECT COUNT(*)
    FROM sys.tables
    WHERE [object_id] IN
    (
        OBJECT_ID(N'dbo.DelphiLivePolicyVersion'),
        OBJECT_ID(N'dbo.DelphiLivePolicyAssignment'),
        OBJECT_ID(N'dbo.DelphiLiveHostLease'),
        OBJECT_ID(N'dbo.DelphiLiveSession'),
        OBJECT_ID(N'dbo.DelphiLiveSessionPolicy'),
        OBJECT_ID(N'dbo.DelphiLiveContinuityEpoch'),
        OBJECT_ID(N'dbo.DelphiLiveSessionSymbol'),
        OBJECT_ID(N'dbo.DelphiLiveFrozenCandidate'),
        OBJECT_ID(N'dbo.DelphiLiveFrozenCandidateLens'),
        OBJECT_ID(N'dbo.DelphiLiveDailyBaseline'),
        OBJECT_ID(N'dbo.IntradayCollectionCycle'),
        OBJECT_ID(N'dbo.IntradayCollectionSlot'),
        OBJECT_ID(N'dbo.IntradayEvidenceConflict'),
        OBJECT_ID(N'dbo.IntradayCollectionReceipt')
    )
) <> 14
    THROW 51222, 'The complete Delphi Live identity and collection foundation was not created.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.DelphiLivePolicyAssignment'),
        OBJECT_ID(N'dbo.DelphiLiveSession'),
        OBJECT_ID(N'dbo.DelphiLiveSessionPolicy'),
        OBJECT_ID(N'dbo.DelphiLiveContinuityEpoch'),
        OBJECT_ID(N'dbo.DelphiLiveSessionSymbol'),
        OBJECT_ID(N'dbo.DelphiLiveFrozenCandidate'),
        OBJECT_ID(N'dbo.DelphiLiveFrozenCandidateLens'),
        OBJECT_ID(N'dbo.DelphiLiveDailyBaseline'),
        OBJECT_ID(N'dbo.IntradayCollectionCycle'),
        OBJECT_ID(N'dbo.IntradayCollectionSlot'),
        OBJECT_ID(N'dbo.IntradayEvidenceConflict'),
        OBJECT_ID(N'dbo.IntradayCollectionReceipt')
    )
      AND ([is_disabled] = 1 OR [is_not_trusted] = 1)
)
    THROW 51223, 'A Delphi Live foundation foreign key is disabled or untrusted.', 1;

IF
(
    SELECT COUNT(*)
    FROM dbo.DelphiLivePolicyVersion
    WHERE [DelphiLivePolicyVersionId] = @PolicyVersionId
      AND [PolicyDefinitionName] = N'DelphiLivePolicyV1'
      AND [PolicyDefinitionSchemaVersion] = 1
      AND [EvaluatorVersion] = N'DelphiLiveEvaluatorV1'
      AND [CollectorVersion] = N'IntradayEvidenceCollectorV3'
      AND [CollectorSourceContractVersion] = 1
      AND [DecisionDossierVersion] = N'DelphiLiveDecisionDossierV1'
      AND [DecisionDossierSchemaVersion] = 1
      AND [QuoteFillVersion] = N'DelphiLiveQuoteFillV1'
      AND [ShadowPortfolioVersion] = N'DelphiLiveShadowPortfolioV1'
      AND [ResearchOutcomeVersion] = N'LiveObservationOutcomeV1'
      AND [RankingDiagnosticVersion] = N'DelphiLiveDailyVsLiveTop5V1'
      AND [PromotionProtocolVersion] = N'DelphiLivePromotionV1'
      AND [SettingsJson] = @SettingsJson
      AND [SettingsEncoding] = N'UTF-8'
      AND [SettingsSha256] = @SettingsSha256
      AND [InitialActivationState] = N'Inactive'
      AND [DecisionRef] = N'ADR-0053'
) <> 1
    THROW 51224, 'The fixed inactive Delphi Live V1 definition does not match ADR-0053.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.DelphiLivePolicyVersion) <> 1
    OR EXISTS (SELECT 1 FROM dbo.DelphiLivePolicyAssignment)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveHostLease)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveSession)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveSessionPolicy)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveContinuityEpoch)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveSessionSymbol)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveFrozenCandidate)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveFrozenCandidateLens)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveDailyBaseline)
    OR EXISTS (SELECT 1 FROM dbo.IntradayCollectionCycle)
    OR EXISTS (SELECT 1 FROM dbo.IntradayCollectionSlot)
    OR EXISTS (SELECT 1 FROM dbo.IntradayEvidenceConflict)
    OR EXISTS (SELECT 1 FROM dbo.IntradayCollectionReceipt)
    THROW 51225, 'The migration created unexpected Delphi Live operational rows.', 1;

IF EXISTS
(
    SELECT 1
    FROM #PreMigrationCounts AS beforeCounts
    INNER JOIN
    (
        SELECT N'StrategyVersion' AS [ObjectName], COUNT_BIG(*) AS [RowCount] FROM dbo.StrategyVersion
        UNION ALL SELECT N'CalibrationRun', COUNT_BIG(*) FROM dbo.CalibrationRun
        UNION ALL SELECT N'CalibrationCandidate', COUNT_BIG(*) FROM dbo.CalibrationCandidate
        UNION ALL SELECT N'CalibrationLensEvaluation', COUNT_BIG(*) FROM dbo.CalibrationLensEvaluation
        UNION ALL SELECT N'IntradayPollObservation', COUNT_BIG(*) FROM dbo.IntradayPollObservation
        UNION ALL SELECT N'IntradayEvidenceBar', COUNT_BIG(*) FROM dbo.IntradayEvidenceBar
    ) AS afterCounts ON afterCounts.[ObjectName] = beforeCounts.[ObjectName]
    WHERE afterCounts.[RowCount] <> beforeCounts.[RowCount]
)
    THROW 51226, 'An existing strategy, calibration, or intraday evidence row count changed.', 1;

COMMIT TRANSACTION;
GO

SELECT
    [DelphiLivePolicyVersionId],
    [PolicyDefinitionName],
    [EvaluatorVersion],
    [CollectorVersion],
    [InitialActivationState],
    CONVERT(VARCHAR(64), [SettingsSha256], 2) AS [SettingsSha256]
FROM dbo.DelphiLivePolicyVersion
WHERE [DelphiLivePolicyVersionId] = 'C15C1A27-13A1-581A-8912-06C92941A01E';
GO
