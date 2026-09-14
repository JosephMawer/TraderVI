CREATE TABLE dbo.DelphiLiveDailySourceRevision
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
        AND WorkingTreeState IN(N'Clean',N'Dirty',N'Unknown'))
);
GO
CREATE INDEX IX_DelphiLiveDailySourceRevision_Latest ON dbo.DelphiLiveDailySourceRevision(SessionId,AdoptedUtc DESC,RevisionId);
GO
CREATE TRIGGER dbo.TR_DelphiLiveDailySourceRevision_Immutable ON dbo.DelphiLiveDailySourceRevision
AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52321,'Daily source adoption history is append-only.',1;
END;
GO
CREATE TRIGGER dbo.TR_DelphiLiveDailySourceRevision_Source ON dbo.DelphiLiveDailySourceRevision
AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.DelphiLiveSession s ON s.SessionId=i.SessionId
        JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId JOIN dbo.StrategyVersion v ON v.VersionId=r.StrategyVersionId
        WHERE r.RecommendationDate<>s.TradingDate OR r.MarketDataAsOf<>s.ExpectedPriorCanonicalSessionDate
        OR r.CreatedUtc<>i.PublishedUtc OR r.RunPurpose<>N'OfficialPaper' OR r.AuditState<>N'Valid' OR v.IsActive<>1
        OR i.AdoptedUtc<s.SessionOpenUtc OR i.AdoptedUtc>=s.SessionCloseUtc)
        OR EXISTS(SELECT 1 FROM inserted i JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId WHERE r.StrategyVersionId IS NULL)
        THROW 52323,'Live adoption requires a valid, current official source within its session.',1;
END;
GO
