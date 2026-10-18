CREATE TABLE dbo.PaperComparisonAccount
(
    AccountId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PaperComparisonAccount PRIMARY KEY,
    BatchId UNIQUEIDENTIFIER NOT NULL,
    PairId UNIQUEIDENTIFIER NOT NULL,
    StrategyCode NVARCHAR(32) NOT NULL,
    GateProfile NVARCHAR(32) NOT NULL,
    ShadowPortfolioId UNIQUEIDENTIFIER NULL,
    LivePortfolioId UNIQUEIDENTIFIER NULL,
    LivePolicyVersionId UNIQUEIDENTIFIER NULL,
    ShadowStrategyVersionId UNIQUEIDENTIFIER NULL,
    ShadowSettingsJson NVARCHAR(MAX) NULL,
    CONSTRAINT FK_PaperComparisonAccount_Batch FOREIGN KEY(BatchId) REFERENCES dbo.PaperComparisonBatch(BatchId),
    CONSTRAINT FK_PaperComparisonAccount_Shadow FOREIGN KEY(ShadowPortfolioId) REFERENCES dbo.ShadowPortfolio(PortfolioId),
    CONSTRAINT FK_PaperComparisonAccount_Live FOREIGN KEY(LivePortfolioId) REFERENCES dbo.DelphiLivePortfolioLedger(PortfolioId),
    CONSTRAINT FK_PaperComparisonAccount_Policy FOREIGN KEY(LivePolicyVersionId) REFERENCES dbo.DelphiLivePolicyVersion(DelphiLivePolicyVersionId),
    CONSTRAINT UQ_PaperComparisonAccount_Pair UNIQUE(PairId,GateProfile),
    CONSTRAINT UQ_PaperComparisonAccount_Strategy UNIQUE(BatchId,StrategyCode,GateProfile),
    CONSTRAINT CK_PaperComparisonAccount_Profile CHECK(GateProfile COLLATE Latin1_General_100_BIN2 IN(N'NormalV1',N'PortfolioRulesOnlyV1')),
    CONSTRAINT CK_PaperComparisonAccount_Identity CHECK
      ((StrategyCode IN(N'ContinuationTop3',N'ContinuationTop5',N'BreakoutTop3',N'BreakoutTop5')
        AND ShadowPortfolioId IS NOT NULL AND AccountId=ShadowPortfolioId AND LivePortfolioId IS NULL AND LivePolicyVersionId IS NULL
        AND ShadowSettingsJson IS NOT NULL AND ISJSON(ShadowSettingsJson)=1)
       OR (StrategyCode=N'DelphiLive' AND LivePortfolioId IS NOT NULL AND AccountId=LivePortfolioId
        AND LivePolicyVersionId IS NOT NULL AND ShadowPortfolioId IS NULL AND ShadowSettingsJson IS NULL AND ShadowStrategyVersionId IS NULL))
);
GO
CREATE TRIGGER dbo.TR_PaperComparisonAccount_Immutable ON dbo.PaperComparisonAccount AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52401,'Comparison account gate profile and provenance are immutable; create a fresh pair.',1;
END;
GO
CREATE TRIGGER dbo.TR_EngineStrategyAssignment_FixedComparison ON dbo.EngineStrategyAssignment AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PaperComparisonAccount a ON a.AccountId=i.TargetId
      WHERE NOT EXISTS(SELECT 1 FROM dbo.ProgressiveProfitProtectionComparisonSuccessor allowed
        WHERE allowed.AccountId=a.AccountId AND allowed.VersionId=i.VersionId))
        THROW 52405,'Comparison accounts permit only the ADR-0090 progressive protection successor; other paired rules stay fixed.',1;
END;
GO
CREATE TRIGGER dbo.TR_DelphiLivePortfolioLedger_FixedComparison ON dbo.DelphiLivePortfolioLedger AFTER UPDATE AS
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
CREATE TRIGGER dbo.TR_PaperComparisonAccount_Pair ON dbo.PaperComparisonAccount AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PaperComparisonAccount a ON a.PairId=i.PairId
       WHERE a.BatchId<>i.BatchId OR a.StrategyCode<>i.StrategyCode)
       THROW 52402,'A comparison pair must belong to one batch and strategy.',1;
END;
GO
