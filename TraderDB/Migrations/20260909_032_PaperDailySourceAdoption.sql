-- ADR-0066. Operational source adoption; original freezes, candidates, fills and holdings are retained.
-- Reviewed manual migration only. Require a fresh checksum-verified backup and a stopped WPF host.
-- No run is adopted by this script. The updated, leased runtime adopts the latest valid source.
-- Recovery: transaction rolls back on any failure. After application, retain the additive schema;
-- stop the updated host before reverting application binaries. Never erase adoption history.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>N'TraderDB' THROW 52330,'Expected TraderDB.',1;
IF OBJECT_ID(N'dbo.ShadowDailySourceRevision',N'U') IS NOT NULL
 OR OBJECT_ID(N'dbo.DelphiLiveDailySourceRevision',N'U') IS NOT NULL
 OR COL_LENGTH(N'dbo.ShadowPortfolioCandidate',N'SourceRevisionId') IS NOT NULL
 OR COL_LENGTH(N'dbo.DelphiLiveSessionSymbol',N'IsAdoptedDailyCandidate') IS NOT NULL
 THROW 52331,'Source-adoption schema already exists or is partially installed.',1;
IF OBJECT_ID(N'dbo.ShadowPortfolioSession',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLiveSession',N'U') IS NULL
 OR OBJECT_ID(N'dbo.UQ_ShadowPortfolioCandidate_SessionRank',N'UQ') IS NULL
 OR OBJECT_ID(N'dbo.UQ_ShadowPortfolioCandidate_SessionSymbol',N'UQ') IS NULL
 OR OBJECT_ID(N'dbo.CK_DelphiLiveSessionSymbol_Source',N'C') IS NULL
 THROW 52332,'Required source schema does not match the reviewed migration.',1;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @LockResult INT;
 EXEC @LockResult=sys.sp_getapplock @Resource=N'TraderVI.EngineSettings',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=30000;
 IF @LockResult<0 THROW 52333,'Stop the monitoring host before migrating.',1;
 EXEC @LockResult=sys.sp_getapplock @Resource=N'DelphiLiveCollectionV1',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=30000;
 IF @LockResult<0 THROW 52334,'Delphi Live collection is still running.',1;
 EXEC(N'CREATE TABLE dbo.ShadowDailySourceRevision
(
    RevisionId UNIQUEIDENTIFIER NOT NULL,
    SessionId UNIQUEIDENTIFIER NOT NULL,
    CalibrationRunId UNIQUEIDENTIFIER NOT NULL,
    PreviousRunId UNIQUEIDENTIFIER NULL,
    PublishedUtc DATETIME2 NOT NULL,
    AdoptedUtc DATETIME2 NOT NULL,
    EffectiveUtc DATETIME2 NOT NULL,
    CONSTRAINT PK_ShadowDailySourceRevision PRIMARY KEY(RevisionId),
    CONSTRAINT FK_ShadowDailySourceRevision_Session FOREIGN KEY(SessionId) REFERENCES dbo.ShadowPortfolioSession(SessionId),
    CONSTRAINT FK_ShadowDailySourceRevision_Run FOREIGN KEY(CalibrationRunId) REFERENCES dbo.CalibrationRun(RunId),
    CONSTRAINT FK_ShadowDailySourceRevision_Previous FOREIGN KEY(PreviousRunId) REFERENCES dbo.CalibrationRun(RunId),
    CONSTRAINT UQ_ShadowDailySourceRevision_Run UNIQUE(SessionId,CalibrationRunId),
    CONSTRAINT UQ_ShadowDailySourceRevision_Identity UNIQUE(RevisionId,SessionId),
    CONSTRAINT CK_ShadowDailySourceRevision_Causality CHECK(PublishedUtc<=AdoptedUtc AND AdoptedUtc<=EffectiveUtc
        AND EffectiveUtc<DATEADD(MINUTE,5,AdoptedUtc) AND DATEPART(MINUTE,EffectiveUtc)%5=0
        AND DATEPART(SECOND,EffectiveUtc)=0 AND DATEPART(NANOSECOND,EffectiveUtc)=0
        AND (PreviousRunId IS NULL OR PreviousRunId<>CalibrationRunId))
);');
 EXEC(N'CREATE INDEX IX_ShadowDailySourceRevision_Latest ON dbo.ShadowDailySourceRevision(SessionId,AdoptedUtc DESC,RevisionId);');
 EXEC(N'CREATE TRIGGER dbo.TR_ShadowDailySourceRevision_Immutable ON dbo.ShadowDailySourceRevision
AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52320,''Daily source adoption history is append-only.'',1;
END;');
 EXEC(N'CREATE TRIGGER dbo.TR_ShadowDailySourceRevision_Source ON dbo.ShadowDailySourceRevision
AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.ShadowPortfolioSession s ON s.SessionId=i.SessionId
        JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId JOIN dbo.StrategyVersion v ON v.VersionId=r.StrategyVersionId
        WHERE r.RecommendationDate<>s.TradingDate OR r.MarketDataAsOf>=s.TradingDate OR r.CreatedUtc<>i.PublishedUtc
        OR r.RunPurpose<>N''OfficialPaper'' OR r.AuditState<>N''Valid'' OR v.IsActive<>1)
        OR EXISTS(SELECT 1 FROM inserted i JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId WHERE r.StrategyVersionId IS NULL)
        THROW 52322,''Shadow adoption requires a valid, current official source for the session.'',1;
END;');
 EXEC(N'CREATE TABLE dbo.DelphiLiveDailySourceRevision
(
    RevisionId UNIQUEIDENTIFIER NOT NULL,
    SessionId UNIQUEIDENTIFIER NOT NULL,
    CalibrationRunId UNIQUEIDENTIFIER NOT NULL,
    PreviousRunId UNIQUEIDENTIFIER NULL,
    PublishedUtc DATETIME2 NOT NULL,
    AdoptedUtc DATETIME2 NOT NULL,
    EffectiveUtc DATETIME2 NOT NULL,
    SnapshotJson NVARCHAR(MAX) NOT NULL,
    SnapshotSha256 BINARY(32) NOT NULL,
    CodeCommit NVARCHAR(128) NOT NULL,
    WorkingTreeState NVARCHAR(16) NOT NULL,
    CONSTRAINT PK_DelphiLiveDailySourceRevision PRIMARY KEY(RevisionId),
    CONSTRAINT FK_DelphiLiveDailySourceRevision_Session FOREIGN KEY(SessionId) REFERENCES dbo.DelphiLiveSession(SessionId),
    CONSTRAINT FK_DelphiLiveDailySourceRevision_Run FOREIGN KEY(CalibrationRunId) REFERENCES dbo.CalibrationRun(RunId),
    CONSTRAINT FK_DelphiLiveDailySourceRevision_Previous FOREIGN KEY(PreviousRunId) REFERENCES dbo.CalibrationRun(RunId),
    CONSTRAINT UQ_DelphiLiveDailySourceRevision_Run UNIQUE(SessionId,CalibrationRunId),
    CONSTRAINT CK_DelphiLiveDailySourceRevision_Causality CHECK(PublishedUtc<=AdoptedUtc AND AdoptedUtc<=EffectiveUtc
        AND EffectiveUtc<DATEADD(MINUTE,5,AdoptedUtc) AND DATEPART(MINUTE,EffectiveUtc)%5=0
        AND DATEPART(SECOND,EffectiveUtc)=0 AND DATEPART(NANOSECOND,EffectiveUtc)=0
        AND (PreviousRunId IS NULL OR PreviousRunId<>CalibrationRunId)),
    CONSTRAINT CK_DelphiLiveDailySourceRevision_Snapshot CHECK(ISJSON(SnapshotJson)=1 AND LEN(CodeCommit) BETWEEN 7 AND 128
        AND WorkingTreeState IN(N''Clean'',N''Dirty'',N''Unknown''))
);');
 EXEC(N'CREATE INDEX IX_DelphiLiveDailySourceRevision_Latest ON dbo.DelphiLiveDailySourceRevision(SessionId,AdoptedUtc DESC,RevisionId);');
 EXEC(N'CREATE TRIGGER dbo.TR_DelphiLiveDailySourceRevision_Immutable ON dbo.DelphiLiveDailySourceRevision
AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52321,''Daily source adoption history is append-only.'',1;
END;');
 EXEC(N'CREATE TRIGGER dbo.TR_DelphiLiveDailySourceRevision_Source ON dbo.DelphiLiveDailySourceRevision
AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DelphiLiveSession s ON s.SessionId=i.SessionId
        JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId JOIN dbo.StrategyVersion v ON v.VersionId=r.StrategyVersionId
        WHERE r.RecommendationDate<>s.TradingDate OR r.MarketDataAsOf<>s.ExpectedPriorCanonicalSessionDate
        OR r.CreatedUtc<>i.PublishedUtc OR r.RunPurpose<>N''OfficialPaper'' OR r.AuditState<>N''Valid'' OR v.IsActive<>1
        OR i.AdoptedUtc<s.SessionOpenUtc OR i.AdoptedUtc>=s.SessionCloseUtc)
        OR EXISTS(SELECT 1 FROM inserted i JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId WHERE r.StrategyVersionId IS NULL)
        THROW 52323,''Live adoption requires a valid, current official source within its session.'',1;
END;');
 EXEC(N'ALTER TABLE dbo.ShadowPortfolioCandidate ADD SourceRevisionId UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.ShadowPortfolioCandidate DROP CONSTRAINT UQ_ShadowPortfolioCandidate_SessionRank,UQ_ShadowPortfolioCandidate_SessionSymbol;
ALTER TABLE dbo.DelphiLiveSessionSymbol ADD IsAdoptedDailyCandidate BIT NOT NULL CONSTRAINT DF_DelphiLiveSessionSymbol_Adopted DEFAULT(0) WITH VALUES;
ALTER TABLE dbo.DelphiLiveSessionSymbol DROP CONSTRAINT CK_DelphiLiveSessionSymbol_Source;');
 EXEC(N'ALTER TABLE dbo.ShadowPortfolioCandidate ADD
 CONSTRAINT FK_ShadowPortfolioCandidate_SourceRevision FOREIGN KEY(SourceRevisionId,SessionId) REFERENCES dbo.ShadowDailySourceRevision(RevisionId,SessionId),
 CONSTRAINT UQ_ShadowPortfolioCandidate_SessionRank UNIQUE(SessionId,SourceRevisionId,Rank),
 CONSTRAINT UQ_ShadowPortfolioCandidate_SessionSymbol UNIQUE(SessionId,SourceRevisionId,Symbol);
ALTER TABLE dbo.DelphiLiveSessionSymbol ADD CONSTRAINT CK_DelphiLiveSessionSymbol_Source CHECK
 (IsFrozenDailyCandidate=1 OR IsAdoptedDailyCandidate=1 OR IsXiuBenchmark=1 OR IsTrackedHolding=1 OR IsDelphiLiveHolding=1 OR HasPendingProtectiveSell=1 OR IsSessionCarryCandidate=1);');
 COMMIT TRANSACTION;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;

