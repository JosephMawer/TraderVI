CREATE TABLE [dbo].[DelphiLiveHostLease]
(
    [LeaseId]              UNIQUEIDENTIFIER NOT NULL,
    [LeaseName]            NVARCHAR(64)     NOT NULL,
    [OwnerId]              NVARCHAR(128)    NOT NULL,
    [FencingToken]         BIGINT           NOT NULL,
    [CollectorVersion]     NVARCHAR(64)     NOT NULL,
    [SourceContractVersion] INT             NOT NULL,
    [CodeCommit]           NVARCHAR(128)    NOT NULL,
    [WorkingTreeState]     NVARCHAR(16)     NOT NULL,
    [AcquiredUtc]          DATETIME2        NOT NULL,
    [LastRenewedUtc]       DATETIME2        NOT NULL,
    [ExpiresUtc]           DATETIME2        NOT NULL,
    [IsHeld]               BIT              NOT NULL,
    [ReleasedUtc]          DATETIME2        NULL,
    [LeaseLostUtc]         DATETIME2        NULL,
    [CreatedUtc]           DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveHostLease_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [RowVersion]           ROWVERSION       NOT NULL,

    CONSTRAINT [PK_DelphiLiveHostLease] PRIMARY KEY CLUSTERED ([LeaseId]),
    CONSTRAINT [UQ_DelphiLiveHostLease_FencingToken] UNIQUE ([LeaseName], [FencingToken]),
    CONSTRAINT [UQ_DelphiLiveHostLease_EpochIdentity] UNIQUE ([LeaseId], [OwnerId], [FencingToken]),
    CONSTRAINT [CK_DelphiLiveHostLease_Name] CHECK ([LeaseName] = N'DelphiLiveMonitor'),
    CONSTRAINT [CK_DelphiLiveHostLease_Identity] CHECK
    (
        LEN(LTRIM(RTRIM([OwnerId]))) > 0
        AND LEN(LTRIM(RTRIM([CollectorVersion]))) > 0
        AND [SourceContractVersion] > 0
        AND LEN(LTRIM(RTRIM([CodeCommit]))) BETWEEN 7 AND 128
        AND [WorkingTreeState] IN (N'Clean', N'Dirty', N'Unknown')
        AND [FencingToken] > 0
    ),
    CONSTRAINT [CK_DelphiLiveHostLease_Time] CHECK
    (
        [LastRenewedUtc] >= [AcquiredUtc]
        AND [ExpiresUtc] > [LastRenewedUtc]
    ),
    CONSTRAINT [CK_DelphiLiveHostLease_TerminalState] CHECK
    (
        ([IsHeld] = 1 AND [ReleasedUtc] IS NULL AND [LeaseLostUtc] IS NULL)
        OR
        (
            [IsHeld] = 0
            AND
            (
                ([ReleasedUtc] IS NOT NULL AND [LeaseLostUtc] IS NULL AND [ReleasedUtc] >= [AcquiredUtc])
                OR
                ([ReleasedUtc] IS NULL AND [LeaseLostUtc] IS NOT NULL AND [LeaseLostUtc] >= [AcquiredUtc])
            )
        )
    )
);
GO

CREATE UNIQUE INDEX [UX_DelphiLiveHostLease_SingleHolder]
    ON [dbo].[DelphiLiveHostLease] ([LeaseName])
    WHERE [IsHeld] = 1;
GO

CREATE INDEX [IX_DelphiLiveHostLease_Owner]
    ON [dbo].[DelphiLiveHostLease] ([OwnerId], [AcquiredUtc] DESC)
    INCLUDE ([FencingToken], [ExpiresUtc], [IsHeld]);
GO
