CREATE TABLE [dbo].[DelphiLiveFrozenCandidate]
(
    [FrozenCandidateId]              UNIQUEIDENTIFIER NOT NULL,
    [SessionId]                      UNIQUEIDENTIFIER NOT NULL,
    [SessionSymbolId]                UNIQUEIDENTIFIER NOT NULL,
    [CalibrationRunId]               UNIQUEIDENTIFIER NOT NULL,
    [CalibrationCandidateId]         UNIQUEIDENTIFIER NOT NULL,
    [DailyStrategyVersionId]         UNIQUEIDENTIFIER NOT NULL,
    [ObservationDate]                DATE             NOT NULL,
    [ObservationOpen]                REAL             NOT NULL,
    [ObservationHigh]                REAL             NOT NULL,
    [ObservationLow]                 REAL             NOT NULL,
    [ObservationClose]               REAL             NOT NULL,
    [ObservationVolume]              BIGINT           NOT NULL,
    [DirectionEdge]                  FLOAT            NOT NULL,
    [CommonCompositeScore]           FLOAT            NOT NULL,
    [CandidateSnapshotSchemaVersion] INT              NOT NULL,
    [CandidateSnapshotJson]          NVARCHAR(MAX)    NOT NULL,
    [CalibrationCandidateCreatedUtc] DATETIME2        NOT NULL,
    [FrozenUtc]                      DATETIME2        NOT NULL,
    [CreatedUtc]                     DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveFrozenCandidate_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLiveFrozenCandidate] PRIMARY KEY CLUSTERED ([FrozenCandidateId]),
    CONSTRAINT [FK_DelphiLiveFrozenCandidate_SessionSymbol] FOREIGN KEY ([SessionSymbolId], [SessionId])
        REFERENCES [dbo].[DelphiLiveSessionSymbol] ([SessionSymbolId], [SessionId]),
    CONSTRAINT [FK_DelphiLiveFrozenCandidate_SessionRun] FOREIGN KEY ([SessionId], [CalibrationRunId])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId], [CalibrationRunId]),
    CONSTRAINT [FK_DelphiLiveFrozenCandidate_SessionStrategy] FOREIGN KEY ([SessionId], [DailyStrategyVersionId])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId], [DailyStrategyVersionId]),
    CONSTRAINT [FK_DelphiLiveFrozenCandidate_ObservationDate] FOREIGN KEY ([SessionId], [ObservationDate])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId], [ExpectedPriorCanonicalSessionDate]),
    -- Freeze repositories must select the candidate, its run, and the session
    -- strategy together; the legacy candidate table has no composite alternate key.
    CONSTRAINT [FK_DelphiLiveFrozenCandidate_CalibrationCandidate] FOREIGN KEY ([CalibrationCandidateId])
        REFERENCES [dbo].[CalibrationCandidate] ([CandidateId]),
    CONSTRAINT [UQ_DelphiLiveFrozenCandidate_SessionSymbol] UNIQUE ([SessionSymbolId]),
    CONSTRAINT [UQ_DelphiLiveFrozenCandidate_SourceCandidate] UNIQUE ([CalibrationCandidateId]),
    CONSTRAINT [UQ_DelphiLiveFrozenCandidate_LensIdentity] UNIQUE ([FrozenCandidateId], [CalibrationCandidateId]),
    CONSTRAINT [CK_DelphiLiveFrozenCandidate_Observation] CHECK
    (
        [ObservationOpen] > 0
        AND [ObservationHigh] > 0
        AND [ObservationLow] > 0
        AND [ObservationClose] > 0
        AND [ObservationLow] <= [ObservationOpen]
        AND [ObservationLow] <= [ObservationClose]
        AND [ObservationHigh] >= [ObservationOpen]
        AND [ObservationHigh] >= [ObservationClose]
        AND [ObservationLow] <= [ObservationHigh]
        AND [ObservationVolume] >= 0
    ),
    CONSTRAINT [CK_DelphiLiveFrozenCandidate_Snapshot] CHECK
    (
        [CandidateSnapshotSchemaVersion] > 0
        AND ISJSON([CandidateSnapshotJson]) = 1
        AND [FrozenUtc] >= [CalibrationCandidateCreatedUtc]
    )
);
GO

CREATE INDEX [IX_DelphiLiveFrozenCandidate_Session]
    ON [dbo].[DelphiLiveFrozenCandidate] ([SessionId], [CommonCompositeScore] DESC)
    INCLUDE ([SessionSymbolId], [CalibrationCandidateId], [DailyStrategyVersionId]);
GO
