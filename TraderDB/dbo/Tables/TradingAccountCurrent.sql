-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingAccountCurrent]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [PermissionId] uniqueidentifier NOT NULL,
    [StrategyId] uniqueidentifier NOT NULL,
    [AssignmentId] uniqueidentifier NOT NULL,
    [BaselineId] uniqueidentifier NOT NULL,
    [Phase] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ExecutionEnabled] bit NOT NULL,
    [NewBuysStopped] bit NOT NULL,
    [TradingDate] date NOT NULL,
    [SessionReference] decimal(19,6) NOT NULL,
    [DrawdownReference] decimal(19,6) NOT NULL,
    [HighestClosingValue] decimal(19,6) NULL,
    [RiskEvidenceId] uniqueidentifier NOT NULL,
    [RiskThroughUtc] datetime2(7) NOT NULL,
    [DailyLossHold] bit NOT NULL,
    [CapitalReviewRequired] bit NOT NULL,
    [CommittedPurchaseCost] decimal(19,6) NOT NULL,
    [ReservedPurchaseCost] decimal(19,6) NOT NULL,
    [ObservedCash] decimal(19,6) NULL,
    [ObservedBuyingPower] decimal(19,6) NULL,
    [BrokerObservedUtc] datetime2(7) NULL,
    [BrokerCursor] nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    [LastAppliedBrokerFactId] nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT [PK_TradingAccountCurrent] PRIMARY KEY ([BindingId]),
    CONSTRAINT [CK_TradingAccountCurrent_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingAccountCurrent_Settings] FOREIGN KEY ([BindingId],[PermissionId]) REFERENCES [dbo].[TradingApprovedSettings]([BindingId],[PermissionId]),
    CONSTRAINT [CK_TradingAccountCurrent_1] CHECK ([Phase] IN (N'Transition',N'Active',N'Paused',N'NeedsAttention')),
    CONSTRAINT [CK_TradingAccountCurrent_2] CHECK ([CommittedPurchaseCost]>=0 AND [ReservedPurchaseCost]>=0)
);
GO
