CREATE TABLE [dbo].[CalibrationLensEvaluation]
(
    [LensEvaluationId] UNIQUEIDENTIFIER NOT NULL,
    [CandidateId]      UNIQUEIDENTIFIER NOT NULL,
    [Lens]             NVARCHAR(16)     NOT NULL,
    [Direction]        NVARCHAR(8)      NOT NULL,
    [IsEligible]       BIT              NOT NULL,
    [Rank]             INT              NOT NULL,
    [RankingKey]       FLOAT            NOT NULL,
    [IsPublished]      BIT              NOT NULL,
    [FirstFailedGate]  NVARCHAR(64)     NULL,
    [TraceSchemaVersion] INT            NOT NULL,
    [GateTraceJson]    NVARCHAR(MAX)    NOT NULL,
    [CreatedUtc]       DATETIME2        NOT NULL CONSTRAINT [DF_CalibrationLensEvaluation_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_CalibrationLensEvaluation] PRIMARY KEY CLUSTERED ([LensEvaluationId]),
    CONSTRAINT [FK_CalibrationLensEvaluation_Candidate] FOREIGN KEY ([CandidateId]) REFERENCES [dbo].[CalibrationCandidate] ([CandidateId]),
    CONSTRAINT [UQ_CalibrationLensEvaluation_CandidateLens] UNIQUE ([CandidateId], [Lens]),
    CONSTRAINT [CK_CalibrationLensEvaluation_Lens] CHECK ([Lens] IN ('Continuation', 'Breakout'))
);
GO

CREATE INDEX [IX_CalibrationLensEvaluation_LensRank]
    ON [dbo].[CalibrationLensEvaluation] ([Lens], [IsPublished], [Rank]);
GO
