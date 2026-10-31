-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingApprovedSettings]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [PermissionId] uniqueidentifier NOT NULL,
    [StrategyId] uniqueidentifier NOT NULL,
    [AssignmentId] uniqueidentifier NOT NULL,
    [SourceId] uniqueidentifier NOT NULL,
    [BaselineId] uniqueidentifier NOT NULL,
    [ImplementationIdentity] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ScopeCode] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ApprovedUtc] datetime2(7) NOT NULL,
    [ExpiresUtc] datetime2(7) NULL,
    [PurchaseCeiling] decimal(19,6) NOT NULL,
    [BuyAllowed] bit NOT NULL,
    [SellAllowed] bit NOT NULL,
    [ConfigurationJson] nvarchar(4000) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_TradingApprovedSettings] PRIMARY KEY ([BindingId],[PermissionId]),
    CONSTRAINT [CK_TradingApprovedSettings_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingApprovedSettings_Binding] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[BrokerAccountImportBinding]([BindingId]),
    CONSTRAINT [CK_TradingApprovedSettings_1] CHECK ([PurchaseCeiling]>=0),
    CONSTRAINT [CK_TradingApprovedSettings_2] CHECK ([ExpiresUtc] IS NULL OR [ExpiresUtc]>=[ApprovedUtc]),
    CONSTRAINT [CK_TradingApprovedSettings_3] CHECK (ISJSON([ConfigurationJson])=1 AND LEFT(LTRIM([ConfigurationJson]),1)=N'{')
);
GO
