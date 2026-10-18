-- ADR-0090 correction: compare Live settings within their canonical policy-store encoding.
-- Precondition: migration 044 installed. Engine catalogue numeric enums and stored string enums
-- describe the same typed policy; comparing those raw encodings incorrectly rejects a valid successor.
-- Effect: alter only the successor view; all policy properties, implementation metadata and the
-- true opt-in/new identity remain checked against the immutable authoritative Live policy store.
-- No account, profile, assignment, holding, stop, capital or history is rewritten.
-- Recovery: transaction rollback on failure; later reversal requires a reviewed migration.
USE [TraderDB];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ProgressiveProfitProtectionComparisonSuccessor',N'V') IS NULL
 OR OBJECT_ID(N'dbo.IsProgressiveProfitProtectionSuccessor',N'FN') IS NULL
 THROW 51000,'Migration 044 must be installed first.',1;
GO
ALTER VIEW dbo.ProgressiveProfitProtectionComparisonSuccessor
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
WHERE a.LivePortfolioId IS NOT NULL AND v.VersionId<>a.LivePolicyVersionId
    -- The authoritative Live store uses camel-case string enums; the engine catalogue uses
    -- Pascal-case numeric enums. Compare like encodings in the authoritative policy store.
    AND JSON_VALUE(v.SettingsJson,'$.UseProgressiveProfitProtection')=N'true'
    AND TRY_CONVERT(UNIQUEIDENTIFIER,JSON_VALUE(v.SettingsJson,'$.PolicyVersionId'))=v.VersionId
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
IF OBJECT_ID(N'dbo.ProgressiveProfitProtectionComparisonSuccessor',N'V') IS NULL
 THROW 51000,'Canonical successor view is missing.',1;
COMMIT;
GO