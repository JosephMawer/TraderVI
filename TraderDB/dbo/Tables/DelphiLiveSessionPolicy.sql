CREATE TABLE [dbo].[DelphiLiveSessionPolicy]
(
    [SessionPolicyId]                 UNIQUEIDENTIFIER NOT NULL,
    [SessionId]                       UNIQUEIDENTIFIER NOT NULL,
    [AssignmentId]                    UNIQUEIDENTIFIER NOT NULL,
    [DelphiLivePolicyVersionId]       UNIQUEIDENTIFIER NOT NULL,
    [DailyStrategyVersionId]          UNIQUEIDENTIFIER NULL,
    [PolicyRole]                      NVARCHAR(32)     NOT NULL,
    [RoleSlot]                        TINYINT          NOT NULL,
    [ExperimentId]                    UNIQUEIDENTIFIER NULL,
    [IsOperationallyEnabled]          BIT              NOT NULL,
    [PolicySettingsJson]              NVARCHAR(MAX)    NOT NULL,
    [PolicySettingsSha256]            BINARY(32)       NOT NULL,
    [FrozenUtc]                       DATETIME2        NOT NULL,
    [CreatedUtc]                      DATETIME2        NOT NULL CONSTRAINT [DF_DelphiLiveSessionPolicy_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_DelphiLiveSessionPolicy] PRIMARY KEY CLUSTERED ([SessionPolicyId]),
    CONSTRAINT [FK_DelphiLiveSessionPolicy_Session] FOREIGN KEY ([SessionId])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId]),
    CONSTRAINT [FK_DelphiLiveSessionPolicy_SessionStrategy] FOREIGN KEY ([SessionId], [DailyStrategyVersionId])
        REFERENCES [dbo].[DelphiLiveSession] ([SessionId], [DailyStrategyVersionId]),
    CONSTRAINT [FK_DelphiLiveSessionPolicy_Assignment] FOREIGN KEY
        ([AssignmentId], [DelphiLivePolicyVersionId], [PolicyRole], [RoleSlot])
        REFERENCES [dbo].[DelphiLivePolicyAssignment]
        ([AssignmentId], [DelphiLivePolicyVersionId], [PolicyRole], [RoleSlot]),
    CONSTRAINT [FK_DelphiLiveSessionPolicy_PolicySettings] FOREIGN KEY
        ([DelphiLivePolicyVersionId], [PolicySettingsSha256])
        REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId], [SettingsSha256]),
    -- Repository freeze writes must copy ExperimentId and SettingsJson from the
    -- matched assignment/policy rows in the same serializable transaction.
    CONSTRAINT [UQ_DelphiLiveSessionPolicy_RoleSlot] UNIQUE ([SessionId], [RoleSlot]),
    CONSTRAINT [UQ_DelphiLiveSessionPolicy_Identity] UNIQUE
        ([SessionPolicyId], [SessionId], [DelphiLivePolicyVersionId], [DailyStrategyVersionId]),
    CONSTRAINT [CK_DelphiLiveSessionPolicy_Role] CHECK
    (
        ([PolicyRole] = N'OperationalChampion' AND [RoleSlot] = 0)
        OR
        ([PolicyRole] IN (N'ActiveShadowChallenger', N'ShadowBaseline') AND [RoleSlot] IN (1, 2))
        OR
        ([PolicyRole] = N'ResearchCounterfactual' AND [RoleSlot] >= 100)
    ),
    CONSTRAINT [CK_DelphiLiveSessionPolicy_Settings] CHECK (ISJSON([PolicySettingsJson]) = 1),
    CONSTRAINT [CK_DelphiLiveSessionPolicy_OperationalRole] CHECK
    (
        [IsOperationallyEnabled] = 0
        OR [PolicyRole] IN (N'OperationalChampion', N'ActiveShadowChallenger', N'ShadowBaseline')
    )
);
GO

CREATE INDEX [IX_DelphiLiveSessionPolicy_Policy]
    ON [dbo].[DelphiLiveSessionPolicy] ([DelphiLivePolicyVersionId], [SessionId])
    INCLUDE ([DailyStrategyVersionId], [PolicyRole], [RoleSlot], [IsOperationallyEnabled]);
GO
