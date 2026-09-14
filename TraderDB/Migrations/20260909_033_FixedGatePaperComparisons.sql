-- ADR-0067. Manual additive migration; requires a fresh verified checksum backup.
-- Existing account/source/trade rows are retained. No account activation is performed here.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>N'TraderDB' THROW 52434,'Expected TraderDB.',1;
IF OBJECT_ID(N'dbo.PaperComparisonBatch',N'U') IS NOT NULL THROW 52430,'Comparison migration already exists; inspect state instead of replaying it.',1;
IF OBJECT_ID(N'dbo.DelphiLiveDailySourceRevision',N'U') IS NULL OR OBJECT_ID(N'dbo.ShadowDailySourceRevision',N'U') IS NULL
 THROW 52431,'Migration 032 is required before fixed-gate comparisons.',1;
IF COL_LENGTH(N'dbo.ShadowPortfolioGeneration',N'IsGateComparison') IS NOT NULL OR COL_LENGTH(N'dbo.DelphiLivePortfolioGeneration',N'ComparisonBatchId') IS NOT NULL
 THROW 52432,'Unexpected partial comparison schema.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_DelphiLivePolicyAssignment_Role')
 OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_DelphiLivePortfolioGeneration_Role')
 THROW 52433,'Existing Live role constraints differ from the reviewed schema.',1;
BEGIN TRANSACTION;
DECLARE @LockResult INT;
EXEC @LockResult=sys.sp_getapplock @Resource=N'TraderVI.EngineSettings',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=30000;
IF @LockResult<0 THROW 52435,'Stop the monitoring host before migrating.',1;
EXEC @LockResult=sys.sp_getapplock @Resource=N'DelphiLiveCollectionV1',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=30000;
IF @LockResult<0 THROW 52436,'Delphi Live collection is still running.',1;
GO
CREATE TABLE dbo.PaperComparisonBatch
(
    BatchId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PaperComparisonBatch PRIMARY KEY,
    BatchKey NVARCHAR(80) NOT NULL CONSTRAINT UQ_PaperComparisonBatch_Key UNIQUE,
    DailyStrategyVersionId UNIQUEIDENTIFIER NOT NULL,
    StartingCapital DECIMAL(19,6) NOT NULL,
    Currency CHAR(3) NOT NULL,
    AuthorizedUtc DATETIME2 NOT NULL,
    EffectiveUtc DATETIME2 NOT NULL,
    TradingDate DATE NOT NULL,
    AuthorizedBy NVARCHAR(128) NOT NULL,
    CONSTRAINT FK_PaperComparisonBatch_Strategy FOREIGN KEY(DailyStrategyVersionId) REFERENCES dbo.StrategyVersion(VersionId),
    CONSTRAINT CK_PaperComparisonBatch_Capital CHECK(StartingCapital=1000 AND Currency='CAD'),
    CONSTRAINT CK_PaperComparisonBatch_Time CHECK(AuthorizedUtc<EffectiveUtc AND DATEPART(MINUTE,EffectiveUtc)%5=0
        AND DATEPART(SECOND,EffectiveUtc)=0 AND DATEPART(NANOSECOND,EffectiveUtc)=0),
    CONSTRAINT CK_PaperComparisonBatch_Identity CHECK(LEN(BatchKey)>0 AND LEN(AuthorizedBy)>0)
);
GO
CREATE TRIGGER dbo.TR_PaperComparisonBatch_Immutable ON dbo.PaperComparisonBatch AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52400,'Comparison batch identity and starting capital are immutable.',1;
END;
GO
ALTER TABLE dbo.ShadowPortfolioGeneration ADD IsGateComparison BIT NOT NULL CONSTRAINT DF_ShadowPortfolioGeneration_GateComparison DEFAULT(0);
ALTER TABLE dbo.DelphiLivePortfolioGeneration ADD ComparisonBatchId UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE dbo.DelphiLivePortfolioGeneration ADD CONSTRAINT FK_DelphiLivePortfolioGeneration_Comparison
 FOREIGN KEY(ComparisonBatchId) REFERENCES dbo.PaperComparisonBatch(BatchId);
ALTER TABLE dbo.DelphiLivePolicyAssignment DROP CONSTRAINT CK_DelphiLivePolicyAssignment_Role;
ALTER TABLE dbo.DelphiLivePolicyAssignment WITH CHECK ADD CONSTRAINT CK_DelphiLivePolicyAssignment_Role CHECK
 ((PolicyRole=N'OperationalChampion' AND RoleSlot=0) OR
 (PolicyRole IN(N'ActiveShadowChallenger',N'ShadowBaseline') AND RoleSlot IN(1,2)) OR
 (PolicyRole=N'GateComparison' AND RoleSlot BETWEEN 3 AND 99 AND ExperimentId IS NULL) OR
 (PolicyRole=N'ResearchCounterfactual' AND RoleSlot>=100));
ALTER TABLE dbo.DelphiLivePortfolioGeneration DROP CONSTRAINT CK_DelphiLivePortfolioGeneration_Role;
ALTER TABLE dbo.DelphiLivePortfolioGeneration WITH CHECK ADD CONSTRAINT CK_DelphiLivePortfolioGeneration_Role CHECK
 ((PortfolioRole=N'OperationalChampion' AND ExperimentId IS NULL AND ComparisonBatchId IS NULL) OR
 (PortfolioRole IN(N'ActiveShadowChallenger',N'ShadowBaseline',N'ChampionControl') AND ExperimentId IS NOT NULL AND ComparisonBatchId IS NULL) OR
 (PortfolioRole=N'GateComparison' AND ExperimentId IS NULL AND ComparisonBatchId IS NOT NULL));
GO
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
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PaperComparisonAccount a ON a.AccountId=i.TargetId)
        THROW 52405,'Comparison accounts retain fixed paired rules; create a fresh pair.',1;
END;
GO
CREATE TRIGGER dbo.TR_DelphiLivePortfolioLedger_FixedComparison ON dbo.DelphiLivePortfolioLedger AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PaperComparisonAccount a ON a.LivePortfolioId=i.PortfolioId
      WHERE i.DelphiLivePolicyVersionId<>a.LivePolicyVersionId
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

CREATE TABLE dbo.PaperComparisonSourceRevision
(
    RevisionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PaperComparisonSourceRevision PRIMARY KEY,
    BatchId UNIQUEIDENTIFIER NOT NULL,
    SessionId UNIQUEIDENTIFIER NOT NULL,
    CalibrationRunId UNIQUEIDENTIFIER NOT NULL,
    PublishedUtc DATETIME2 NOT NULL,
    AdoptedUtc DATETIME2 NOT NULL,
    EffectiveUtc DATETIME2 NOT NULL,
    SnapshotJson NVARCHAR(MAX) NOT NULL,
    SnapshotSha256 BINARY(32) NOT NULL,
    CONSTRAINT FK_PaperComparisonSourceRevision_Batch FOREIGN KEY(BatchId) REFERENCES dbo.PaperComparisonBatch(BatchId),
    CONSTRAINT FK_PaperComparisonSourceRevision_Session FOREIGN KEY(SessionId) REFERENCES dbo.DelphiLiveSession(SessionId),
    CONSTRAINT FK_PaperComparisonSourceRevision_Run FOREIGN KEY(CalibrationRunId) REFERENCES dbo.CalibrationRun(RunId),
    CONSTRAINT UQ_PaperComparisonSourceRevision_Source UNIQUE(BatchId,SessionId,CalibrationRunId),
    CONSTRAINT CK_PaperComparisonSourceRevision_Snapshot CHECK(ISJSON(SnapshotJson)=1),
    CONSTRAINT CK_PaperComparisonSourceRevision_Time CHECK(PublishedUtc<=AdoptedUtc AND AdoptedUtc<=EffectiveUtc
        AND DATEPART(MINUTE,EffectiveUtc)%5=0 AND DATEPART(SECOND,EffectiveUtc)=0 AND DATEPART(NANOSECOND,EffectiveUtc)=0)
);
GO
CREATE INDEX IX_PaperComparisonSourceRevision_Latest ON dbo.PaperComparisonSourceRevision(BatchId,SessionId,AdoptedUtc DESC);
GO
CREATE TRIGGER dbo.TR_PaperComparisonSourceRevision_Immutable ON dbo.PaperComparisonSourceRevision AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52403,'Comparison source evidence is append-only.',1;
END;
GO
CREATE TRIGGER dbo.TR_PaperComparisonSourceRevision_Valid ON dbo.PaperComparisonSourceRevision AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.PaperComparisonBatch b ON b.BatchId=i.BatchId
      JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId JOIN dbo.DelphiLiveSession s ON s.SessionId=i.SessionId
      WHERE r.StrategyVersionId IS NULL OR r.StrategyVersionId<>b.DailyStrategyVersionId
        OR r.RecommendationDate<>s.TradingDate OR r.MarketDataAsOf<>s.ExpectedPriorCanonicalSessionDate
        OR r.RunPurpose<>N'OfficialPaper' OR r.AuditState<>N'Valid' OR r.CreatedUtc<>i.PublishedUtc
        OR i.AdoptedUtc<s.SessionOpenUtc OR i.AdoptedUtc>=s.SessionCloseUtc OR i.EffectiveUtc<b.EffectiveUtc
        OR i.EffectiveUtc>s.SessionCloseUtc)
        THROW 52404,'Comparison source does not match its assigned strategy, date or prospective boundary.',1;
END;
GO

COMMIT TRANSACTION;
GO

