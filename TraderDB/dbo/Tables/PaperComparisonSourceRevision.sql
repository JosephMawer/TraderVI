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
