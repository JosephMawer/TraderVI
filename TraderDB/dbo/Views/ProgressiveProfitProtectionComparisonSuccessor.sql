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
