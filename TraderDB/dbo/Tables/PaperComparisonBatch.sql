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
