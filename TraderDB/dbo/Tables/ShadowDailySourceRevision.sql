CREATE TABLE dbo.ShadowDailySourceRevision
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
);
GO
CREATE INDEX IX_ShadowDailySourceRevision_Latest ON dbo.ShadowDailySourceRevision(SessionId,AdoptedUtc DESC,RevisionId);
GO
CREATE TRIGGER dbo.TR_ShadowDailySourceRevision_Immutable ON dbo.ShadowDailySourceRevision
AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 52320,'Daily source adoption history is append-only.',1;
END;
GO
CREATE TRIGGER dbo.TR_ShadowDailySourceRevision_Source ON dbo.ShadowDailySourceRevision
AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.ShadowPortfolioSession s ON s.SessionId=i.SessionId
        JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId JOIN dbo.StrategyVersion v ON v.VersionId=r.StrategyVersionId
        WHERE r.RecommendationDate<>s.TradingDate OR r.MarketDataAsOf>=s.TradingDate OR r.CreatedUtc<>i.PublishedUtc
        OR r.RunPurpose<>N'OfficialPaper' OR r.AuditState<>N'Valid' OR v.IsActive<>1)
        OR EXISTS(SELECT 1 FROM inserted i JOIN dbo.CalibrationRun r ON r.RunId=i.CalibrationRunId WHERE r.StrategyVersionId IS NULL)
        THROW 52322,'Shadow adoption requires a valid, current official source for the session.',1;
END;
GO
