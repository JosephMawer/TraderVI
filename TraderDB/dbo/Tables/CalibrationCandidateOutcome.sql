CREATE TABLE [dbo].[CalibrationCandidateOutcome]
(
    [CandidateOutcomeId]   UNIQUEIDENTIFIER NOT NULL,
    [CandidateId]          UNIQUEIDENTIFIER NOT NULL,
    [OutcomeDefinitionId]  UNIQUEIDENTIFIER NOT NULL,
    [MaturityState]        NVARCHAR(16)     NOT NULL,
    [AuditState]           NVARCHAR(16)     NOT NULL,
    [OutcomeJson]          NVARCHAR(MAX)    NOT NULL,
    [CreatedUtc]           DATETIME2        NOT NULL CONSTRAINT [DF_CalibrationCandidateOutcome_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_CalibrationCandidateOutcome] PRIMARY KEY CLUSTERED ([CandidateOutcomeId]),
    CONSTRAINT [FK_CalibrationCandidateOutcome_Candidate] FOREIGN KEY ([CandidateId]) REFERENCES [dbo].[CalibrationCandidate] ([CandidateId]),
    CONSTRAINT [FK_CalibrationCandidateOutcome_Definition] FOREIGN KEY ([OutcomeDefinitionId]) REFERENCES [dbo].[CalibrationOutcomeDefinition] ([OutcomeDefinitionId]),
    CONSTRAINT [UQ_CalibrationCandidateOutcome_CandidateDefinition] UNIQUE ([CandidateId], [OutcomeDefinitionId]),
    CONSTRAINT [CK_CalibrationCandidateOutcome_Maturity] CHECK ([MaturityState] IN ('Pending', 'Matured', 'NoEntry')),
    CONSTRAINT [CK_CalibrationCandidateOutcome_Audit] CHECK ([AuditState] IN ('Valid', 'Degraded', 'Invalid'))
);
GO

CREATE INDEX [IX_CalibrationCandidateOutcome_DefinitionMaturity]
    ON [dbo].[CalibrationCandidateOutcome] ([OutcomeDefinitionId], [MaturityState]);
GO
