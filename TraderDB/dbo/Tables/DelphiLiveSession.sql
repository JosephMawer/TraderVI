CREATE TABLE [dbo].[DelphiLiveSession]
(
    [SessionId]                          UNIQUEIDENTIFIER NOT NULL,
    [TradingDate]                        DATE             NOT NULL,
    [SessionOpenUtc]                     DATETIME2        NOT NULL,
    [SessionCloseUtc]                    DATETIME2        NOT NULL,
    [FreezeBoundaryUtc]                  DATETIME2        NOT NULL,
    [FrozenUtc]                          DATETIME2        NOT NULL,
    [ExpectedPriorCanonicalSessionDate]  DATE             NOT NULL,
    [FreezeStatus]                       NVARCHAR(32)     NOT NULL,
    [CalibrationRunId]                   UNIQUEIDENTIFIER NULL,
    [DailyStrategyVersionId]             UNIQUEIDENTIFIER NULL,
    [CalibrationRunPurpose]              NVARCHAR(32)     NULL,
    [CalibrationRunAuditState]           NVARCHAR(16)     NULL,
    [CalibrationRecommendationDate]      DATE             NULL,
    [CalibrationMarketDataAsOf]          DATE             NULL,
    [CalibrationRunStartedUtc]           DATETIME2        NULL,
    [CalibrationRunCreatedUtc]           DATETIME2        NULL,
    [CollectorVersion]                   NVARCHAR(64)     NOT NULL,
    [CollectorSourceContractVersion]     INT              NOT NULL,
    [CalendarVersion]                    NVARCHAR(64)     NOT NULL,
    [CodeCommit]                         NVARCHAR(128)    NOT NULL,
    [WorkingTreeState]                   NVARCHAR(16)     NOT NULL,
    [SessionState]                       NVARCHAR(32)     NOT NULL,
    [CoverageState]                      NVARCHAR(16)     NOT NULL,
    [HostGapObserved]                    BIT              NOT NULL CONSTRAINT [DF_DelphiLiveSession_HostGap] DEFAULT ((0)),
    [EngineeringTrialId]                 UNIQUEIDENTIFIER NULL,
    [CompletedUtc]                       DATETIME2        NULL,
    [CreatedUtc]                         DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveSession_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]                         DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveSession_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLiveSession] PRIMARY KEY CLUSTERED ([SessionId]),
    CONSTRAINT [FK_DelphiLiveSession_CalibrationRun] FOREIGN KEY ([CalibrationRunId])
        REFERENCES [dbo].[CalibrationRun] ([RunId]),
    CONSTRAINT [FK_DelphiLiveSession_DailyStrategyVersion] FOREIGN KEY ([DailyStrategyVersionId])
        REFERENCES [dbo].[StrategyVersion] ([VersionId]),
    CONSTRAINT [FK_DelphiLiveSession_TrialDate] FOREIGN KEY ([EngineeringTrialId], [TradingDate])
        REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId], [TradingDate]),
    CONSTRAINT [FK_DelphiLiveSession_TrialRun] FOREIGN KEY ([EngineeringTrialId], [CalibrationRunId])
        REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId], [CalibrationRunId]),
    CONSTRAINT [UQ_DelphiLiveSession_TradingDate] UNIQUE ([TradingDate]),
    CONSTRAINT [UQ_DelphiLiveSession_SourceThroughDate] UNIQUE ([SessionId], [ExpectedPriorCanonicalSessionDate]),
    CONSTRAINT [UQ_DelphiLiveSession_RunIdentity] UNIQUE ([SessionId], [CalibrationRunId]),
    CONSTRAINT [UQ_DelphiLiveSession_DailyStrategyIdentity] UNIQUE ([SessionId], [DailyStrategyVersionId]),
    CONSTRAINT [CK_DelphiLiveSession_Bounds] CHECK
    (
        [SessionOpenUtc] < [SessionCloseUtc]
        AND [FreezeBoundaryUtc] = [SessionOpenUtc]
        AND [FrozenUtc] >= [FreezeBoundaryUtc]
        AND [ExpectedPriorCanonicalSessionDate] < [TradingDate]
    ),
    CONSTRAINT [CK_DelphiLiveSession_FreezeStatus] CHECK ([FreezeStatus] IN (N'FrozenOfficialRun', N'NoValidDelphiRun', N'OperatorEngineeringTest')),
    CONSTRAINT [CK_DelphiLiveSession_FrozenRun] CHECK
    (
        (
            [FreezeStatus] = N'FrozenOfficialRun'
            AND [EngineeringTrialId] IS NULL
            AND [CalibrationRunId] IS NOT NULL
            AND [DailyStrategyVersionId] IS NOT NULL
            AND [CalibrationRunPurpose] = N'OfficialPaper'
            AND [CalibrationRunAuditState] = N'Valid'
            AND [CalibrationRecommendationDate] = [TradingDate]
            AND [CalibrationMarketDataAsOf] = [ExpectedPriorCanonicalSessionDate]
            AND [CalibrationRunStartedUtc] IS NOT NULL
            AND [CalibrationRunCreatedUtc] IS NOT NULL
            AND [CalibrationRunCreatedUtc] <= [FreezeBoundaryUtc]
        )
        OR
        (
            [FreezeStatus] = N'NoValidDelphiRun'
            AND [EngineeringTrialId] IS NULL
            AND [CalibrationRunId] IS NULL
            AND [DailyStrategyVersionId] IS NULL
            AND [CalibrationRunPurpose] IS NULL
            AND [CalibrationRunAuditState] IS NULL
            AND [CalibrationRecommendationDate] IS NULL
            AND [CalibrationMarketDataAsOf] IS NULL
            AND [CalibrationRunStartedUtc] IS NULL
            AND [CalibrationRunCreatedUtc] IS NULL
        )
        OR
        (
            [FreezeStatus] = N'OperatorEngineeringTest'
            AND [EngineeringTrialId] IS NOT NULL
            AND [EngineeringTrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908'
            AND [TradingDate] = '20260908' AND [HostGapObserved] = 1
            AND [CalibrationRunId] = '9491e421-ef3e-48b4-bcb7-389c42ec25e9'
            AND [DailyStrategyVersionId] IS NOT NULL
            AND [CalibrationRunPurpose] = N'OfficialPaper' AND [CalibrationRunAuditState] = N'Valid'
            AND [CalibrationRecommendationDate] = [TradingDate]
            AND [CalibrationMarketDataAsOf] = [ExpectedPriorCanonicalSessionDate]
            AND [CalibrationRunStartedUtc] IS NOT NULL AND [CalibrationRunCreatedUtc] IS NOT NULL
            AND [CalibrationRunCreatedUtc] > [FreezeBoundaryUtc] AND [CalibrationRunCreatedUtc] <= [FrozenUtc]
        )
    ),
    CONSTRAINT [CK_DelphiLiveSession_Collector] CHECK
    (
        LEN(LTRIM(RTRIM([CollectorVersion]))) > 0
        AND [CollectorSourceContractVersion] > 0
        AND LEN(LTRIM(RTRIM([CalendarVersion]))) > 0
        AND LEN(LTRIM(RTRIM([CodeCommit]))) BETWEEN 7 AND 128
        AND [WorkingTreeState] IN (N'Clean', N'Dirty', N'Unknown')
    ),
    CONSTRAINT [CK_DelphiLiveSession_State] CHECK
    (
        [SessionState] IN (N'Frozen', N'Monitoring', N'Completed', N'Incomplete')
        AND [CoverageState] IN (N'Pending', N'Ready', N'Degraded', N'Blocked')
        AND
        (
            ([SessionState] IN (N'Frozen', N'Monitoring') AND [CompletedUtc] IS NULL)
            OR
            ([SessionState] IN (N'Completed', N'Incomplete') AND [CompletedUtc] IS NOT NULL AND [CompletedUtc] >= [FrozenUtc])
        )
    )
);
GO

CREATE INDEX [IX_DelphiLiveSession_State]
    ON [dbo].[DelphiLiveSession] ([TradingDate] DESC, [SessionState], [CoverageState]);
GO
