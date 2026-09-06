CREATE TABLE [dbo].[DelphiLivePolicyVersion]
(
    [DelphiLivePolicyVersionId]       UNIQUEIDENTIFIER NOT NULL,
    [PolicyDefinitionName]            NVARCHAR(64)     NOT NULL,
    [PolicyDefinitionSchemaVersion]   INT              NOT NULL,
    [EvaluatorVersion]                NVARCHAR(64)     NOT NULL,
    [CollectorVersion]                NVARCHAR(64)     NOT NULL,
    [CollectorSourceContractVersion]  INT              NOT NULL,
    [DecisionDossierVersion]          NVARCHAR(64)     NOT NULL,
    [DecisionDossierSchemaVersion]    INT              NOT NULL,
    [QuoteFillVersion]                NVARCHAR(64)     NOT NULL,
    [ShadowPortfolioVersion]          NVARCHAR(64)     NOT NULL,
    [ResearchOutcomeVersion]          NVARCHAR(64)     NOT NULL,
    [RankingDiagnosticVersion]        NVARCHAR(64)     NOT NULL,
    [PromotionProtocolVersion]        NVARCHAR(64)     NOT NULL,
    [SettingsJson]                    NVARCHAR(MAX)    NOT NULL,
    [SettingsEncoding]                NVARCHAR(16)     NOT NULL,
    [SettingsSha256]                  BINARY(32)       NOT NULL,
    [InitialActivationState]          NVARCHAR(16)     NOT NULL,
    [DecisionRef]                     NVARCHAR(64)     NOT NULL,
    [CreatedUtc]                      DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLivePolicyVersion_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLivePolicyVersion] PRIMARY KEY CLUSTERED ([DelphiLivePolicyVersionId]),
    CONSTRAINT [UQ_DelphiLivePolicyVersion_Settings] UNIQUE ([DelphiLivePolicyVersionId], [SettingsSha256]),
    CONSTRAINT [CK_DelphiLivePolicyVersion_Versions] CHECK
    (
        [PolicyDefinitionSchemaVersion] > 0
        AND [CollectorSourceContractVersion] > 0
        AND [DecisionDossierSchemaVersion] > 0
    ),
    CONSTRAINT [CK_DelphiLivePolicyVersion_Identities] CHECK
    (
        LEN(LTRIM(RTRIM([PolicyDefinitionName]))) > 0
        AND LEN(LTRIM(RTRIM([EvaluatorVersion]))) > 0
        AND LEN(LTRIM(RTRIM([CollectorVersion]))) > 0
        AND LEN(LTRIM(RTRIM([DecisionDossierVersion]))) > 0
        AND LEN(LTRIM(RTRIM([QuoteFillVersion]))) > 0
        AND LEN(LTRIM(RTRIM([ShadowPortfolioVersion]))) > 0
        AND LEN(LTRIM(RTRIM([ResearchOutcomeVersion]))) > 0
        AND LEN(LTRIM(RTRIM([RankingDiagnosticVersion]))) > 0
        AND LEN(LTRIM(RTRIM([PromotionProtocolVersion]))) > 0
        AND LEN(LTRIM(RTRIM([DecisionRef]))) > 0
    ),
    CONSTRAINT [CK_DelphiLivePolicyVersion_Settings] CHECK
    (
        [SettingsEncoding] = N'UTF-8'
        AND ISJSON([SettingsJson]) = 1
        AND DATALENGTH([SettingsJson]) > 2
    ),
    CONSTRAINT [CK_DelphiLivePolicyVersion_InactiveInstall] CHECK ([InitialActivationState] = N'Inactive')
);
GO

CREATE INDEX [IX_DelphiLivePolicyVersion_Definition]
    ON [dbo].[DelphiLivePolicyVersion] ([PolicyDefinitionName], [PolicyDefinitionSchemaVersion]);
GO
