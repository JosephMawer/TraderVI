CREATE TABLE [dbo].[CalibrationRun]
(
    [RunId]                   UNIQUEIDENTIFIER NOT NULL,
    [RunPurpose]              NVARCHAR(32)     NOT NULL,
    [RecommendationDate]      DATE             NOT NULL,
    [MarketDataAsOf]          DATE             NOT NULL,
    [StartedUtc]              DATETIME2        NOT NULL,
    [CreatedUtc]              DATETIME2        NOT NULL CONSTRAINT [DF_CalibrationRun_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [StrategyVersionId]       UNIQUEIDENTIFIER NULL,
    [StrategyConfigJson]      NVARCHAR(MAX)    NOT NULL,
    [ModelSnapshotJson]       NVARCHAR(MAX)    NOT NULL,
    [RunContextJson]          NVARCHAR(MAX)    NOT NULL,
    [CodeCommit]              NVARCHAR(128)    NOT NULL,
    [CodeVersionSource]       NVARCHAR(32)     NOT NULL,
    [WorkingTreeState]        NVARCHAR(16)     NOT NULL,
    [FeatureSchemaVersion]    INT              NOT NULL,
    [CandidateSchemaVersion]  INT              NOT NULL,
    [LensSchemaVersion]       INT              NOT NULL,
    [AuditState]              NVARCHAR(16)     NOT NULL,
    [AuditMessage]            NVARCHAR(1024)   NULL,
    [SymbolsDiscovered]       INT              NOT NULL,
    [SymbolsModelEvaluated]   INT              NOT NULL,
    [SkippedHistory]          INT              NOT NULL,
    [SkippedStaleHistory]     INT              NOT NULL,
    [SkippedUnaffordable]     INT              NOT NULL,
    [SkippedLowPrice]         INT              NOT NULL,
    [SkippedLowVolume]        INT              NOT NULL,
    [SkippedLeveragedEtp]     INT              NOT NULL,

    CONSTRAINT [PK_CalibrationRun] PRIMARY KEY CLUSTERED ([RunId]),
    CONSTRAINT [FK_CalibrationRun_StrategyVersion] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
    CONSTRAINT [CK_CalibrationRun_Purpose] CHECK ([RunPurpose] IN ('OfficialPaper', 'ExploratoryReplay', 'LegacyReconstruction')),
    CONSTRAINT [CK_CalibrationRun_AuditState] CHECK ([AuditState] IN ('Valid', 'Degraded', 'Invalid')),
    CONSTRAINT [CK_CalibrationRun_WorkingTreeState] CHECK ([WorkingTreeState] IN ('Clean', 'Dirty', 'Unknown'))
);
GO

CREATE INDEX [IX_CalibrationRun_PurposeDate]
    ON [dbo].[CalibrationRun] ([RunPurpose], [RecommendationDate], [CreatedUtc]);
GO
