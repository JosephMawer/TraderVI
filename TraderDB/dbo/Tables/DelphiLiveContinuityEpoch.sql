CREATE TABLE [dbo].[DelphiLiveContinuityEpoch]
(
    [ContinuityEpochId]           UNIQUEIDENTIFIER NOT NULL,
    [SessionId]                   UNIQUEIDENTIFIER NOT NULL,
    [EpochNumber]                 INT              NOT NULL,
    [PreviousContinuityEpochId]   UNIQUEIDENTIFIER NULL,
    [LeaseId]                     UNIQUEIDENTIFIER NOT NULL,
    [LeaseOwnerId]                NVARCHAR(128)    NOT NULL,
    [LeaseFencingToken]           BIGINT           NOT NULL,
    [BeganAtSessionOpen]          BIT              NOT NULL,
    [StartReason]                 NVARCHAR(32)     NOT NULL,
    [StartedUtc]                  DATETIME2        NOT NULL,
    [OperationalBuffersResetUtc]  DATETIME2        NOT NULL,
    [RestartDispositionJson]      NVARCHAR(MAX)    NOT NULL,
    [EndedUtc]                    DATETIME2        NULL,
    [EndReason]                   NVARCHAR(32)     NULL,
    [CoverageDisposition]         NVARCHAR(32)     NOT NULL,
    [HostGapObserved]             BIT              NOT NULL,
    [CreatedUtc]                  DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveContinuityEpoch_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLiveContinuityEpoch] PRIMARY KEY CLUSTERED ([ContinuityEpochId]),
    CONSTRAINT [FK_DelphiLiveContinuityEpoch_Session] FOREIGN KEY ([SessionId])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId]),
    CONSTRAINT [FK_DelphiLiveContinuityEpoch_Previous] FOREIGN KEY ([PreviousContinuityEpochId], [SessionId])
        REFERENCES [dbo].[DelphiLiveContinuityEpoch] ([ContinuityEpochId], [SessionId]),
    CONSTRAINT [FK_DelphiLiveContinuityEpoch_Lease] FOREIGN KEY ([LeaseId], [LeaseOwnerId], [LeaseFencingToken])
        REFERENCES [dbo].[DelphiLiveHostLease] ([LeaseId], [OwnerId], [FencingToken]),
    CONSTRAINT [UQ_DelphiLiveContinuityEpoch_Number] UNIQUE ([SessionId], [EpochNumber]),
    CONSTRAINT [UQ_DelphiLiveContinuityEpoch_SessionIdentity] UNIQUE ([ContinuityEpochId], [SessionId]),
    CONSTRAINT [UQ_DelphiLiveContinuityEpoch_CycleIdentity] UNIQUE
        ([ContinuityEpochId], [SessionId], [LeaseId], [LeaseFencingToken]),
    CONSTRAINT [CK_DelphiLiveContinuityEpoch_Number] CHECK
    (
        [EpochNumber] > 0
        AND
        (
            ([EpochNumber] = 1 AND [PreviousContinuityEpochId] IS NULL)
            OR ([EpochNumber] > 1 AND [PreviousContinuityEpochId] IS NOT NULL)
        )
    ),
    CONSTRAINT [CK_DelphiLiveContinuityEpoch_Start] CHECK
    (
        [StartReason] IN (N'SessionOpen', N'LateHostStart', N'HostRestart', N'LeaseTakeover', N'OperationalEvidenceGap')
        AND [OperationalBuffersResetUtc] >= [StartedUtc]
        AND ISJSON([RestartDispositionJson]) = 1
        AND (([BeganAtSessionOpen] = 1 AND [StartReason] = N'SessionOpen') OR [BeganAtSessionOpen] = 0)
    ),
    CONSTRAINT [CK_DelphiLiveContinuityEpoch_End] CHECK
    (
        (
            [EndedUtc] IS NULL
            AND [EndReason] IS NULL
            AND [CoverageDisposition] = N'Pending'
        )
        OR
        (
            [EndedUtc] IS NOT NULL
            AND [EndedUtc] >= [StartedUtc]
            AND [EndReason] IN (N'SessionClose', N'HostStopped', N'HostGap', N'LeaseLost', N'CollectorFault')
            AND [CoverageDisposition] IN (N'Complete', N'HostGap', N'LeaseLost', N'CollectorFault', N'StoppedEarly')
        )
    ),
    CONSTRAINT [CK_DelphiLiveContinuityEpoch_HostGap] CHECK
    (
        [HostGapObserved] = 0
        OR [StartReason] IN (N'LateHostStart', N'HostRestart', N'LeaseTakeover', N'OperationalEvidenceGap')
        OR [EndReason] IN (N'HostStopped', N'HostGap', N'LeaseLost', N'CollectorFault')
    )
);
GO

CREATE UNIQUE INDEX [UX_DelphiLiveContinuityEpoch_OpenSession]
    ON [dbo].[DelphiLiveContinuityEpoch] ([SessionId])
    WHERE [EndedUtc] IS NULL;
GO
