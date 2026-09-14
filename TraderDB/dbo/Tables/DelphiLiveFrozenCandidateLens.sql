CREATE TABLE [dbo].[DelphiLiveFrozenCandidateLens]
(
    [FrozenCandidateLensId]       UNIQUEIDENTIFIER NOT NULL,
    [FrozenCandidateId]           UNIQUEIDENTIFIER NOT NULL,
    [CalibrationCandidateId]      UNIQUEIDENTIFIER NOT NULL,
    [CalibrationLensEvaluationId] UNIQUEIDENTIFIER NOT NULL,
    [Lens]                        NVARCHAR(16)     NOT NULL,
    [Direction]                   NVARCHAR(8)      NOT NULL,
    [IsEligible]                  BIT              NOT NULL,
    [FrozenRank]                  INT              NOT NULL,
    [FrozenRankingKey]            FLOAT            NOT NULL,
    [IsPublished]                 BIT              NOT NULL,
    [FirstFailedGate]             NVARCHAR(64)     NULL,
    [TraceSchemaVersion]          INT              NOT NULL,
    [GateTraceJson]               NVARCHAR(MAX)    NOT NULL,
    [CalibrationLensCreatedUtc]   DATETIME2        NOT NULL,
    [FrozenUtc]                   DATETIME2        NOT NULL,
    [CreatedUtc]                  DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveFrozenCandidateLens_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [EngineeringTrialId]          UNIQUEIDENTIFIER NULL,

    CONSTRAINT [PK_DelphiLiveFrozenCandidateLens] PRIMARY KEY CLUSTERED ([FrozenCandidateLensId]),
    CONSTRAINT [FK_DelphiLiveFrozenCandidateLens_Trial] FOREIGN KEY ([EngineeringTrialId])
        REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId]),
    CONSTRAINT [FK_DelphiLiveFrozenCandidateLens_FrozenCandidate] FOREIGN KEY
        ([FrozenCandidateId], [CalibrationCandidateId])
        REFERENCES [dbo].[DelphiLiveFrozenCandidate] ([FrozenCandidateId], [CalibrationCandidateId]),
    -- Freeze repositories must resolve the evaluation ID and candidate/lens
    -- natural key together because the legacy source exposes separate keys.
    CONSTRAINT [FK_DelphiLiveFrozenCandidateLens_SourceEvaluation] FOREIGN KEY ([CalibrationLensEvaluationId])
        REFERENCES [dbo].[CalibrationLensEvaluation] ([LensEvaluationId]),
    CONSTRAINT [FK_DelphiLiveFrozenCandidateLens_SourceLens] FOREIGN KEY ([CalibrationCandidateId], [Lens])
        REFERENCES [dbo].[CalibrationLensEvaluation] ([CandidateId], [Lens]),
    CONSTRAINT [UQ_DelphiLiveFrozenCandidateLens_Lens] UNIQUE ([FrozenCandidateId], [Lens]),
    CONSTRAINT [UQ_DelphiLiveFrozenCandidateLens_SourceEvaluation] UNIQUE ([CalibrationLensEvaluationId]),
    CONSTRAINT [CK_DelphiLiveFrozenCandidateLens_Identity] CHECK
    (
        [Lens] IN (N'Continuation', N'Breakout')
        AND (([EngineeringTrialId] IS NULL AND [IsEligible] = 1)
          OR ([EngineeringTrialId] IS NOT NULL AND [EngineeringTrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908' AND [IsEligible] = 0
            AND [Direction] = N'Hold' AND [FirstFailedGate] = N'Breadth'))
        AND [IsPublished] = 1
        AND [FrozenRank] BETWEEN 1 AND 25
    ),
    CONSTRAINT [CK_DelphiLiveFrozenCandidateLens_Trace] CHECK
    (
        [TraceSchemaVersion] > 0
        AND ISJSON([GateTraceJson]) = 1
        AND [FrozenUtc] >= [CalibrationLensCreatedUtc]
    )
);
GO

CREATE INDEX [IX_DelphiLiveFrozenCandidateLens_Rank]
    ON [dbo].[DelphiLiveFrozenCandidateLens] ([Lens], [FrozenRank], [FrozenRankingKey] DESC)
    INCLUDE ([FrozenCandidateId], [CalibrationCandidateId], [CalibrationLensEvaluationId]);
GO
