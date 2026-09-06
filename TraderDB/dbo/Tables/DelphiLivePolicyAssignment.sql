CREATE TABLE [dbo].[DelphiLivePolicyAssignment]
(
    [AssignmentId]                  UNIQUEIDENTIFIER NOT NULL,
    [DelphiLivePolicyVersionId]     UNIQUEIDENTIFIER NOT NULL,
    [PolicyRole]                    NVARCHAR(32)     NOT NULL,
    [RoleSlot]                      TINYINT          NOT NULL,
    [ExperimentId]                  UNIQUEIDENTIFIER NULL,
    [EffectiveTradingDate]          DATE             NOT NULL,
    [EndExclusiveTradingDate]       DATE             NULL,
    [AuthorizedUtc]                 DATETIME2        NOT NULL,
    [AuthorizedBy]                  NVARCHAR(128)    NOT NULL,
    [AuthorizationReason]           NVARCHAR(1024)   NOT NULL,
    [DecisionRef]                   NVARCHAR(64)     NOT NULL,
    [CancelledUtc]                  DATETIME2        NULL,
    [CancelledBy]                   NVARCHAR(128)    NULL,
    [CancellationReason]            NVARCHAR(1024)   NULL,
    [CreatedUtc]                    DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLivePolicyAssignment_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLivePolicyAssignment] PRIMARY KEY CLUSTERED ([AssignmentId]),
    CONSTRAINT [FK_DelphiLivePolicyAssignment_Policy] FOREIGN KEY ([DelphiLivePolicyVersionId])
        REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId]),
    CONSTRAINT [UQ_DelphiLivePolicyAssignment_FrozenIdentity] UNIQUE
        ([AssignmentId], [DelphiLivePolicyVersionId], [PolicyRole], [RoleSlot]),
    CONSTRAINT [CK_DelphiLivePolicyAssignment_Role] CHECK
    (
        ([PolicyRole] = N'OperationalChampion' AND [RoleSlot] = 0)
        OR
        ([PolicyRole] IN (N'ActiveShadowChallenger', N'ShadowBaseline') AND [RoleSlot] IN (1, 2))
        OR
        ([PolicyRole] = N'ResearchCounterfactual' AND [RoleSlot] >= 100)
    ),
    CONSTRAINT [CK_DelphiLivePolicyAssignment_EffectiveRange] CHECK
    (
        [EndExclusiveTradingDate] IS NULL
        OR [EndExclusiveTradingDate] > [EffectiveTradingDate]
    ),
    CONSTRAINT [CK_DelphiLivePolicyAssignment_Authorization] CHECK
    (
        LEN(LTRIM(RTRIM([AuthorizedBy]))) > 0
        AND LEN(LTRIM(RTRIM([AuthorizationReason]))) > 0
        AND LEN(LTRIM(RTRIM([DecisionRef]))) > 0
    ),
    CONSTRAINT [CK_DelphiLivePolicyAssignment_Cancellation] CHECK
    (
        ([CancelledUtc] IS NULL AND [CancelledBy] IS NULL AND [CancellationReason] IS NULL)
        OR
        (
            [CancelledUtc] IS NOT NULL
            AND [CancelledUtc] >= [AuthorizedUtc]
            AND LEN(LTRIM(RTRIM([CancelledBy]))) > 0
            AND LEN(LTRIM(RTRIM([CancellationReason]))) > 0
        )
    )
);
GO

CREATE UNIQUE INDEX [UX_DelphiLivePolicyAssignment_OpenRoleSlot]
    ON [dbo].[DelphiLivePolicyAssignment] ([RoleSlot])
    WHERE [EndExclusiveTradingDate] IS NULL AND [CancelledUtc] IS NULL;
GO

CREATE INDEX [IX_DelphiLivePolicyAssignment_EffectiveDate]
    ON [dbo].[DelphiLivePolicyAssignment] ([EffectiveTradingDate], [EndExclusiveTradingDate])
    INCLUDE ([DelphiLivePolicyVersionId], [PolicyRole], [RoleSlot], [CancelledUtc]);
GO
