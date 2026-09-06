CREATE TABLE [dbo].[ShadowPortfolioCandidate]
(
    [CandidateTrackingId]    UNIQUEIDENTIFIER NOT NULL,
    [SessionId]              UNIQUEIDENTIFIER NOT NULL,
    [CalibrationCandidateId] UNIQUEIDENTIFIER NOT NULL,
    [Symbol]                 NVARCHAR(20)     NOT NULL,
    [Rank]                   TINYINT          NOT NULL,
    [State]                  NVARCHAR(24)     NOT NULL,
    [ReasonCode]             NVARCHAR(64)     NULL,
    [LastEvaluatedUtc]       DATETIME2        NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolioCandidate_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolioCandidate_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowPortfolioCandidate] PRIMARY KEY CLUSTERED ([CandidateTrackingId]),
    CONSTRAINT [FK_ShadowPortfolioCandidate_Session] FOREIGN KEY ([SessionId]) REFERENCES [dbo].[ShadowPortfolioSession] ([SessionId]),
    CONSTRAINT [FK_ShadowPortfolioCandidate_Candidate] FOREIGN KEY ([CalibrationCandidateId]) REFERENCES [dbo].[CalibrationCandidate] ([CandidateId]),
    CONSTRAINT [FK_ShadowPortfolioCandidate_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols] ([Symbol]),
    CONSTRAINT [UQ_ShadowPortfolioCandidate_SessionRank] UNIQUE ([SessionId], [Rank]),
    CONSTRAINT [UQ_ShadowPortfolioCandidate_SessionSymbol] UNIQUE ([SessionId], [Symbol]),
    CONSTRAINT [CK_ShadowPortfolioCandidate_Rank] CHECK ([Rank] BETWEEN 1 AND 5),
    CONSTRAINT [CK_ShadowPortfolioCandidate_State] CHECK ([State] IN (N'Pending', N'Qualified', N'Entered', N'Held', N'NoEntry', N'Blocked', N'Exited'))
);
GO

CREATE INDEX [IX_ShadowPortfolioCandidate_SessionState]
    ON [dbo].[ShadowPortfolioCandidate] ([SessionId], [State], [Rank]);
GO
