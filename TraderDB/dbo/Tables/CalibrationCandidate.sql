CREATE TABLE [dbo].[CalibrationCandidate]
(
    [CandidateId]          UNIQUEIDENTIFIER NOT NULL,
    [RunId]                UNIQUEIDENTIFIER NOT NULL,
    [Symbol]               NVARCHAR(16)     NOT NULL,
    [ObservationDate]      DATE             NOT NULL,
    [ObservationOpen]      REAL             NOT NULL,
    [ObservationHigh]      REAL             NOT NULL,
    [ObservationLow]       REAL             NOT NULL,
    [ObservationClose]     REAL             NOT NULL,
    [ObservationVolume]    BIGINT           NOT NULL,
    [UpProbability]        FLOAT            NULL,
    [DownProbability]      FLOAT            NULL,
    [BreakoutProbability]  FLOAT            NULL,
    [VolExpansionProbability] FLOAT         NULL,
    [DirectionEdge]        FLOAT            NOT NULL,
    [CompositeScore]       FLOAT            NOT NULL,
    [RsCompositeScore]     FLOAT            NULL,
    [RsCompositeScoreZ]    FLOAT            NULL,
    [ObvState]             NVARCHAR(24)     NULL,
    [ObvTilt]              FLOAT            NOT NULL,
    [SnapshotSchemaVersion] INT             NOT NULL,
    [SnapshotJson]         NVARCHAR(MAX)    NOT NULL,
    [CreatedUtc]           DATETIME2        NOT NULL CONSTRAINT [DF_CalibrationCandidate_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_CalibrationCandidate] PRIMARY KEY CLUSTERED ([CandidateId]),
    CONSTRAINT [FK_CalibrationCandidate_Run] FOREIGN KEY ([RunId]) REFERENCES [dbo].[CalibrationRun] ([RunId]),
    CONSTRAINT [UQ_CalibrationCandidate_RunSymbol] UNIQUE ([RunId], [Symbol])
);
GO

CREATE INDEX [IX_CalibrationCandidate_Run]
    ON [dbo].[CalibrationCandidate] ([RunId], [Symbol]);
GO
