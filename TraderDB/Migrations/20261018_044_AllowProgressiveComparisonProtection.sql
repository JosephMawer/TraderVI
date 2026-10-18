-- ADR-0090 narrow prospective protection extension. Prepared for explicitly authorized rollout.
-- Preconditions: migration 043 and original fixed-comparison triggers; successor objects absent.
-- Data effect: add predicate/function and replace two guards; no account, profile, capital or history rewrite.
-- Recovery: transaction rollback on failure. After installation retain earned state and assignments;
-- a reversal requires a separately reviewed migration, never disable protection or rewrite history.
USE [TraderDB];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ShadowProgressiveProtection',N'U') IS NULL
 OR OBJECT_ID(N'dbo.TR_EngineStrategyAssignment_FixedComparison',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.TR_DelphiLivePortfolioLedger_FixedComparison',N'TR') IS NULL
 OR OBJECT_ID(N'dbo.IsProgressiveProfitProtectionSuccessor') IS NOT NULL
 OR OBJECT_ID(N'dbo.ProgressiveProfitProtectionComparisonSuccessor') IS NOT NULL
 THROW 51000,'Unexpected progressive comparison migration preconditions; inspect before retrying.',1;
GO
CREATE FUNCTION dbo.IsProgressiveProfitProtectionSuccessor
(
    @OriginalSettings NVARCHAR(MAX),
    @OriginalIdentity NVARCHAR(MAX),
    @Candidate NVARCHAR(MAX)
)
RETURNS BIT
AS
BEGIN
    IF ISNULL(ISJSON(@OriginalSettings),0)<>1 OR ISNULL(ISJSON(@Candidate),0)<>1 RETURN 0;
    IF @OriginalIdentity IS NOT NULL AND ISJSON(@OriginalIdentity)<>1 RETURN 0;
    DECLARE @Expected TABLE (PropertyName NVARCHAR(4000), PropertyValue NVARCHAR(MAX), JsonType INT);
    DECLARE @Actual TABLE (PropertyName NVARCHAR(4000), PropertyValue NVARCHAR(MAX), JsonType INT);
    INSERT @Expected
        SELECT LOWER([key]),value,type FROM OPENJSON(@OriginalSettings)
        WHERE LOWER([key])<>N'useprogressiveprofitprotection'
        UNION ALL SELECT LOWER([key]),value,type FROM OPENJSON(COALESCE(@OriginalIdentity,N'{}'));
    INSERT @Actual SELECT LOWER([key]),value,type FROM OPENJSON(@Candidate)
        WHERE LOWER([key])<>N'useprogressiveprofitprotection';
    IF (SELECT COUNT(*) FROM OPENJSON(@Candidate) WHERE LOWER([key])=N'useprogressiveprofitprotection')<>1
        OR NOT EXISTS(SELECT 1 FROM OPENJSON(@Candidate) WHERE LOWER([key])=N'useprogressiveprofitprotection' AND type=3 AND value=N'true') RETURN 0;
    IF EXISTS(SELECT PropertyName FROM @Expected GROUP BY PropertyName HAVING COUNT(*)<>1)
        OR EXISTS(SELECT PropertyName FROM @Actual GROUP BY PropertyName HAVING COUNT(*)<>1) RETURN 0;
    IF EXISTS(SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Expected
        EXCEPT SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Actual)
        OR EXISTS(SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Actual
        EXCEPT SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Expected) RETURN 0;
    RETURN 1;
END;

GO
CREATE VIEW dbo.ProgressiveProfitProtectionComparisonSuccessor
AS
SELECT a.AccountId,v.VersionId
FROM dbo.PaperComparisonAccount a
JOIN dbo.EngineStrategyVersion v ON v.Family=N'SystemShadow'
WHERE a.ShadowPortfolioId IS NOT NULL
    AND dbo.IsProgressiveProfitProtectionSuccessor(a.ShadowSettingsJson,NULL,v.SettingsJson)=1
UNION ALL
SELECT a.AccountId,v.VersionId
FROM dbo.PaperComparisonAccount a
JOIN dbo.EngineStrategyVersion v ON v.Family=N'DelphiLive'
JOIN dbo.DelphiLivePolicyVersion oldPolicy ON oldPolicy.DelphiLivePolicyVersionId=a.LivePolicyVersionId
JOIN dbo.DelphiLivePolicyVersion newPolicy ON newPolicy.DelphiLivePolicyVersionId=v.VersionId
CROSS APPLY(SELECT (SELECT LOWER(CONVERT(NVARCHAR(36),v.VersionId)) PolicyVersionId,oldPolicy.PolicyDefinitionName,oldPolicy.PolicyDefinitionSchemaVersion,
    oldPolicy.EvaluatorVersion,oldPolicy.CollectorVersion,oldPolicy.CollectorSourceContractVersion,
    oldPolicy.DecisionDossierVersion,oldPolicy.DecisionDossierSchemaVersion,oldPolicy.QuoteFillVersion,
    oldPolicy.ShadowPortfolioVersion,oldPolicy.ResearchOutcomeVersion,oldPolicy.RankingDiagnosticVersion,
    oldPolicy.PromotionProtocolVersion FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) OriginalIdentity) identityJson
WHERE a.LivePortfolioId IS NOT NULL AND v.VersionId<>a.LivePolicyVersionId
    AND dbo.IsProgressiveProfitProtectionSuccessor(oldPolicy.SettingsJson,identityJson.OriginalIdentity,v.SettingsJson)=1
    AND dbo.IsProgressiveProfitProtectionSuccessor(oldPolicy.SettingsJson,NULL,newPolicy.SettingsJson)=1
    AND newPolicy.PolicyDefinitionName=oldPolicy.PolicyDefinitionName
    AND newPolicy.PolicyDefinitionSchemaVersion=oldPolicy.PolicyDefinitionSchemaVersion
    AND newPolicy.EvaluatorVersion=oldPolicy.EvaluatorVersion AND newPolicy.CollectorVersion=oldPolicy.CollectorVersion
    AND newPolicy.CollectorSourceContractVersion=oldPolicy.CollectorSourceContractVersion
    AND newPolicy.DecisionDossierVersion=oldPolicy.DecisionDossierVersion
    AND newPolicy.DecisionDossierSchemaVersion=oldPolicy.DecisionDossierSchemaVersion
    AND newPolicy.QuoteFillVersion=oldPolicy.QuoteFillVersion AND newPolicy.ShadowPortfolioVersion=oldPolicy.ShadowPortfolioVersion
    AND newPolicy.ResearchOutcomeVersion=oldPolicy.ResearchOutcomeVersion
    AND newPolicy.RankingDiagnosticVersion=oldPolicy.RankingDiagnosticVersion
    AND newPolicy.PromotionProtocolVersion=oldPolicy.PromotionProtocolVersion
    AND newPolicy.SettingsEncoding=oldPolicy.SettingsEncoding;

GO
ALTER TRIGGER dbo.TR_EngineStrategyAssignment_FixedComparison ON dbo.EngineStrategyAssignment AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PaperComparisonAccount a ON a.AccountId=i.TargetId
      WHERE NOT EXISTS(SELECT 1 FROM dbo.ProgressiveProfitProtectionComparisonSuccessor allowed
        WHERE allowed.AccountId=a.AccountId AND allowed.VersionId=i.VersionId))
        THROW 52405,'Comparison accounts permit only the ADR-0090 progressive protection successor; other paired rules stay fixed.',1;
END;
GO
ALTER TRIGGER dbo.TR_DelphiLivePortfolioLedger_FixedComparison ON dbo.DelphiLivePortfolioLedger AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PaperComparisonAccount a ON a.LivePortfolioId=i.PortfolioId
      WHERE (i.DelphiLivePolicyVersionId<>a.LivePolicyVersionId AND NOT EXISTS
        (SELECT 1 FROM dbo.ProgressiveProfitProtectionComparisonSuccessor allowed
          JOIN dbo.EngineStrategyAssignment assigned ON assigned.TargetId=allowed.AccountId AND assigned.VersionId=allowed.VersionId
          WHERE allowed.AccountId=a.AccountId AND allowed.VersionId=i.DelphiLivePolicyVersionId))
       OR ISNULL(JSON_VALUE(i.SnapshotJson,'$.comparisonAccount.profile'),N'')<>CASE a.GateProfile WHEN N'NormalV1' THEN N'0' ELSE N'1' END
       OR ISNULL(JSON_VALUE(i.SnapshotJson,'$.comparisonAccount.accountId'),N'')<>CONVERT(NVARCHAR(36),a.AccountId)
       OR ISNULL(JSON_VALUE(i.SnapshotJson,'$.comparisonAccount.batchId'),N'')<>CONVERT(NVARCHAR(36),a.BatchId)
       OR ISNULL(JSON_VALUE(i.SnapshotJson,'$.comparisonAccount.pairId'),N'')<>CONVERT(NVARCHAR(36),a.PairId))
        THROW 52406,'Live comparison profile and policy identity cannot change.',1;
END;
GO
IF OBJECT_ID(N'dbo.IsProgressiveProfitProtectionSuccessor',N'FN') IS NULL
 OR OBJECT_ID(N'dbo.ProgressiveProfitProtectionComparisonSuccessor',N'V') IS NULL
 OR EXISTS(SELECT 1 FROM sys.triggers WHERE name IN(N'TR_EngineStrategyAssignment_FixedComparison',N'TR_DelphiLivePortfolioLedger_FixedComparison') AND is_disabled=1)
 THROW 51000,'Progressive comparison migration postcondition failed.',1;
COMMIT;
GO