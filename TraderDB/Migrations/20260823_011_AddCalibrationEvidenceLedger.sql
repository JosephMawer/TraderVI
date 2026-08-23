SET XACT_ABORT ON;

/*
    Paper-calibration schema rollout (ADR-0020 / ADR-0021).

    This is the single manual deployment script for every new SQL object in the
    first calibration release. It creates five tables plus their primary keys,
    foreign keys, checks, unique constraints, defaults, and supporting indexes.

    Review and run only after a verified TraderDB backup. Do not publish a DACPAC.
*/

IF OBJECT_ID(N'dbo.StrategyVersion', N'U') IS NULL
    THROW 51000, 'Prerequisite dbo.StrategyVersion does not exist. Calibration migration was not started.', 1;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.CalibrationRun', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CalibrationRun]
    (
        [RunId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_CalibrationRun] PRIMARY KEY,
        [RunPurpose] NVARCHAR(32) NOT NULL,
        [RecommendationDate] DATE NOT NULL,
        [MarketDataAsOf] DATE NOT NULL,
        [StartedUtc] DATETIME2 NOT NULL,
        [CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_CalibrationRun_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
        [StrategyVersionId] UNIQUEIDENTIFIER NULL,
        [StrategyConfigJson] NVARCHAR(MAX) NOT NULL,
        [ModelSnapshotJson] NVARCHAR(MAX) NOT NULL,
        [RunContextJson] NVARCHAR(MAX) NOT NULL,
        [CodeCommit] NVARCHAR(128) NOT NULL,
        [CodeVersionSource] NVARCHAR(32) NOT NULL,
        [WorkingTreeState] NVARCHAR(16) NOT NULL,
        [FeatureSchemaVersion] INT NOT NULL,
        [CandidateSchemaVersion] INT NOT NULL,
        [LensSchemaVersion] INT NOT NULL,
        [AuditState] NVARCHAR(16) NOT NULL,
        [AuditMessage] NVARCHAR(1024) NULL,
        [SymbolsDiscovered] INT NOT NULL,
        [SymbolsModelEvaluated] INT NOT NULL,
        [SkippedHistory] INT NOT NULL,
        [SkippedStaleHistory] INT NOT NULL,
        [SkippedUnaffordable] INT NOT NULL,
        [SkippedLowPrice] INT NOT NULL,
        [SkippedLowVolume] INT NOT NULL,
        [SkippedLeveragedEtp] INT NOT NULL,
        CONSTRAINT [FK_CalibrationRun_StrategyVersion] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
        CONSTRAINT [CK_CalibrationRun_Purpose] CHECK ([RunPurpose] IN ('OfficialPaper','ExploratoryReplay','LegacyReconstruction')),
        CONSTRAINT [CK_CalibrationRun_AuditState] CHECK ([AuditState] IN ('Valid','Degraded','Invalid')),
        CONSTRAINT [CK_CalibrationRun_WorkingTreeState] CHECK ([WorkingTreeState] IN ('Clean','Dirty','Unknown'))
    );
    CREATE INDEX [IX_CalibrationRun_PurposeDate] ON [dbo].[CalibrationRun] ([RunPurpose], [RecommendationDate], [CreatedUtc]);
END;

IF OBJECT_ID(N'dbo.CalibrationCandidate', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CalibrationCandidate]
    (
        [CandidateId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_CalibrationCandidate] PRIMARY KEY,
        [RunId] UNIQUEIDENTIFIER NOT NULL,
        [Symbol] NVARCHAR(16) NOT NULL,
        [ObservationDate] DATE NOT NULL,
        [ObservationOpen] REAL NOT NULL,
        [ObservationHigh] REAL NOT NULL,
        [ObservationLow] REAL NOT NULL,
        [ObservationClose] REAL NOT NULL,
        [ObservationVolume] BIGINT NOT NULL,
        [UpProbability] FLOAT NULL,
        [DownProbability] FLOAT NULL,
        [BreakoutProbability] FLOAT NULL,
        [VolExpansionProbability] FLOAT NULL,
        [DirectionEdge] FLOAT NOT NULL,
        [CompositeScore] FLOAT NOT NULL,
        [RsCompositeScore] FLOAT NULL,
        [RsCompositeScoreZ] FLOAT NULL,
        [ObvState] NVARCHAR(24) NULL,
        [ObvTilt] FLOAT NOT NULL,
        [SnapshotSchemaVersion] INT NOT NULL,
        [SnapshotJson] NVARCHAR(MAX) NOT NULL,
        [CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_CalibrationCandidate_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [FK_CalibrationCandidate_Run] FOREIGN KEY ([RunId]) REFERENCES [dbo].[CalibrationRun] ([RunId]),
        CONSTRAINT [UQ_CalibrationCandidate_RunSymbol] UNIQUE ([RunId], [Symbol])
    );
    CREATE INDEX [IX_CalibrationCandidate_Run] ON [dbo].[CalibrationCandidate] ([RunId], [Symbol]);
END;

IF OBJECT_ID(N'dbo.CalibrationLensEvaluation', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CalibrationLensEvaluation]
    (
        [LensEvaluationId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_CalibrationLensEvaluation] PRIMARY KEY,
        [CandidateId] UNIQUEIDENTIFIER NOT NULL,
        [Lens] NVARCHAR(16) NOT NULL,
        [Direction] NVARCHAR(8) NOT NULL,
        [IsEligible] BIT NOT NULL,
        [Rank] INT NOT NULL,
        [RankingKey] FLOAT NOT NULL,
        [IsPublished] BIT NOT NULL,
        [FirstFailedGate] NVARCHAR(64) NULL,
        [TraceSchemaVersion] INT NOT NULL,
        [GateTraceJson] NVARCHAR(MAX) NOT NULL,
        [CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_CalibrationLensEvaluation_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [FK_CalibrationLensEvaluation_Candidate] FOREIGN KEY ([CandidateId]) REFERENCES [dbo].[CalibrationCandidate] ([CandidateId]),
        CONSTRAINT [UQ_CalibrationLensEvaluation_CandidateLens] UNIQUE ([CandidateId], [Lens]),
        CONSTRAINT [CK_CalibrationLensEvaluation_Lens] CHECK ([Lens] IN ('Continuation','Breakout'))
    );
    CREATE INDEX [IX_CalibrationLensEvaluation_LensRank] ON [dbo].[CalibrationLensEvaluation] ([Lens], [IsPublished], [Rank]);
END;

IF OBJECT_ID(N'dbo.CalibrationOutcomeDefinition', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CalibrationOutcomeDefinition]
    (
        [OutcomeDefinitionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_CalibrationOutcomeDefinition] PRIMARY KEY,
        [DefinitionName] NVARCHAR(64) NOT NULL,
        [DefinitionVersion] INT NOT NULL,
        [DefinitionKind] NVARCHAR(24) NOT NULL,
        [DefinitionJson] NVARCHAR(MAX) NOT NULL,
        [IsActive] BIT NOT NULL,
        [CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_CalibrationOutcomeDefinition_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [UQ_CalibrationOutcomeDefinition_NameVersion] UNIQUE ([DefinitionName], [DefinitionVersion]),
        CONSTRAINT [CK_CalibrationOutcomeDefinition_Kind] CHECK ([DefinitionKind] IN ('Prediction','Tradeable','Portfolio'))
    );
END;

IF OBJECT_ID(N'dbo.CalibrationCandidateOutcome', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CalibrationCandidateOutcome]
    (
        [CandidateOutcomeId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_CalibrationCandidateOutcome] PRIMARY KEY,
        [CandidateId] UNIQUEIDENTIFIER NOT NULL,
        [OutcomeDefinitionId] UNIQUEIDENTIFIER NOT NULL,
        [MaturityState] NVARCHAR(16) NOT NULL,
        [AuditState] NVARCHAR(16) NOT NULL,
        [OutcomeJson] NVARCHAR(MAX) NOT NULL,
        [CreatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_CalibrationCandidateOutcome_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [FK_CalibrationCandidateOutcome_Candidate] FOREIGN KEY ([CandidateId]) REFERENCES [dbo].[CalibrationCandidate] ([CandidateId]),
        CONSTRAINT [FK_CalibrationCandidateOutcome_Definition] FOREIGN KEY ([OutcomeDefinitionId]) REFERENCES [dbo].[CalibrationOutcomeDefinition] ([OutcomeDefinitionId]),
        CONSTRAINT [UQ_CalibrationCandidateOutcome_CandidateDefinition] UNIQUE ([CandidateId], [OutcomeDefinitionId]),
        CONSTRAINT [CK_CalibrationCandidateOutcome_Maturity] CHECK ([MaturityState] IN ('Pending','Matured','NoEntry')),
        CONSTRAINT [CK_CalibrationCandidateOutcome_Audit] CHECK ([AuditState] IN ('Valid','Degraded','Invalid'))
    );
    CREATE INDEX [IX_CalibrationCandidateOutcome_DefinitionMaturity] ON [dbo].[CalibrationCandidateOutcome] ([OutcomeDefinitionId], [MaturityState]);
END;

IF OBJECT_ID(N'dbo.CalibrationRun', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CalibrationCandidate', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CalibrationLensEvaluation', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CalibrationOutcomeDefinition', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CalibrationCandidateOutcome', N'U') IS NULL
    THROW 51001, 'Calibration schema verification failed. Transaction will be rolled back.', 1;

COMMIT TRANSACTION;

SELECT
    [SchemaName] = s.[name],
    [ObjectName] = o.[name],
    [ObjectType] = o.[type_desc]
FROM sys.objects o
JOIN sys.schemas s ON s.[schema_id] = o.[schema_id]
WHERE s.[name] = N'dbo'
  AND o.[name] IN
  (
      N'CalibrationRun',
      N'CalibrationCandidate',
      N'CalibrationLensEvaluation',
      N'CalibrationOutcomeDefinition',
      N'CalibrationCandidateOutcome'
  )
ORDER BY o.[name];
