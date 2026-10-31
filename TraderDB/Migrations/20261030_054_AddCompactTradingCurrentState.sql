-- ADR-0117. PREPARED ONLY. Not executed by builds, tests or engine startup.
-- Explicit review + fresh verified backup required before installation.
-- Additive empty tables only: no data copy, cleanup, activation or history rewrite.
-- Execute from a reviewed sqlcmd session only; this script deliberately refuses existing/partial objects.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Required session settings for filtered indexes and their later DML.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
DECLARE @backup nvarchar(4000)=N'$(VerifiedBackupFile)';
IF NOT EXISTS(SELECT 1 FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily f ON f.media_set_id=b.media_set_id
 WHERE b.database_name=N'TraderDB' AND b.type='D' AND b.has_backup_checksums=1 AND b.is_damaged=0
 AND b.backup_finish_date>=DATEADD(HOUR,-2,GETDATE()) AND f.physical_device_name IN (@backup,@backup+N'.partial'))
 THROW 54604,'A fresh checksum full backup is required for compact storage installation.',1;
RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.BrokerAccountImportBinding',N'U') IS NULL
 THROW 54605,'The original broker account binding schema is required.',1;
IF OBJECT_ID(N'dbo.TradingApprovedSettings') IS NOT NULL
 OR OBJECT_ID(N'dbo.TradingAccountCurrent') IS NOT NULL
 OR OBJECT_ID(N'dbo.TradingPositionCurrent') IS NOT NULL
 OR OBJECT_ID(N'dbo.TradingSymbolCurrent') IS NOT NULL
 OR OBJECT_ID(N'dbo.TradingOrder') IS NOT NULL
 OR OBJECT_ID(N'dbo.TradingRestrictionCurrent') IS NOT NULL
 OR OBJECT_ID(N'dbo.TradingCheckpoint') IS NOT NULL
 THROW 54606,'Compact storage already exists or is partial. Inspect instead of overwriting.',1;
GO
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

-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingPositionCurrent]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [PositionId] uniqueidentifier NOT NULL,
    [SecurityId] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [IsOpen] bit NOT NULL,
    [Quantity] int NOT NULL,
    [ReservedSellQuantity] int NOT NULL,
    [CostBasis] decimal(19,6) NOT NULL,
    [FirstEntryUtc] datetime2(7) NOT NULL,
    [LastEntryUtc] datetime2(7) NOT NULL,
    [OriginalTarget] decimal(19,6) NULL,
    [EntryCount] int NOT NULL,
    [AddOnCount] int NOT NULL,
    [ProtectionOwner] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ProtectionFloor] decimal(19,6) NULL,
    [HighestClose] decimal(19,6) NULL,
    [ProtectionStage] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [LastProtectionUtc] datetime2(7) NULL,
    [ExitDecisionId] uniqueidentifier NULL,
    [ExitReason] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    [ExitTriggeredUtc] datetime2(7) NULL,
    [ProtectionJson] nvarchar(1024) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_TradingPositionCurrent] PRIMARY KEY ([BindingId],[PositionId]),
    CONSTRAINT [CK_TradingPositionCurrent_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingPositionCurrent_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId]),
    CONSTRAINT [CK_TradingPositionCurrent_1] CHECK ([Quantity]>=0 AND [ReservedSellQuantity]>=0 AND [ReservedSellQuantity]<=[Quantity] AND [CostBasis]>=0 AND [EntryCount]>=0 AND [AddOnCount]>=0),
    CONSTRAINT [CK_TradingPositionCurrent_2] CHECK (([IsOpen]=1 AND [Quantity]>0) OR ([IsOpen]=0 AND [Quantity]=0)),
    CONSTRAINT [CK_TradingPositionCurrent_3] CHECK ([LastEntryUtc]>=[FirstEntryUtc]),
    CONSTRAINT [CK_TradingPositionCurrent_4] CHECK (([ExitDecisionId] IS NULL AND [ExitReason] IS NULL AND [ExitTriggeredUtc] IS NULL) OR ([ExitDecisionId] IS NOT NULL AND [ExitReason] IS NOT NULL AND [ExitTriggeredUtc] IS NOT NULL)),
    CONSTRAINT [CK_TradingPositionCurrent_5] CHECK (ISJSON([ProtectionJson])=1 AND LEFT(LTRIM([ProtectionJson]),1)=N'{'),
    CONSTRAINT [CK_TradingPositionCurrent_Symbol] CHECK (LEN([Symbol])>0 AND [Symbol]=UPPER([Symbol]))
);
GO

-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingSymbolCurrent]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [Symbol] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [IsRelevant] bit NOT NULL,
    [StrategyId] uniqueidentifier NOT NULL,
    [TradingDate] date NOT NULL,
    [SessionId] uniqueidentifier NOT NULL,
    [EntriesToday] int NOT NULL,
    [LastExitUtc] datetime2(7) NULL,
    [LastExitReason] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    [LastProcessedUtc] datetime2(7) NULL,
    [LastInputId] uniqueidentifier NULL,
    [ConfirmationStartedUtc] datetime2(7) NULL,
    [SignalExpiresUtc] datetime2(7) NULL,
    [ProgressJson] nvarchar(1024) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT [PK_TradingSymbolCurrent] PRIMARY KEY ([BindingId],[Symbol]),
    CONSTRAINT [CK_TradingSymbolCurrent_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingSymbolCurrent_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId]),
    CONSTRAINT [CK_TradingSymbolCurrent_1] CHECK ([EntriesToday]>=0),
    CONSTRAINT [CK_TradingSymbolCurrent_2] CHECK ([SignalExpiresUtc] IS NULL OR [ConfirmationStartedUtc] IS NULL OR [SignalExpiresUtc]>=[ConfirmationStartedUtc]),
    CONSTRAINT [CK_TradingSymbolCurrent_3] CHECK (ISJSON([ProgressJson])=1 AND LEFT(LTRIM([ProgressJson]),1)=N'{'),
    CONSTRAINT [CK_TradingSymbolCurrent_Symbol] CHECK (LEN([Symbol])>0 AND [Symbol]=UPPER([Symbol]))
);
GO

-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingOrder]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [RequestId] uniqueidentifier NOT NULL,
    [DecisionId] uniqueidentifier NOT NULL,
    [PermissionId] uniqueidentifier NOT NULL,
    [PositionId] uniqueidentifier NULL,
    [BrokerOrderId] nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    [SecurityId] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Symbol] nvarchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Side] nvarchar(4) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Quantity] int NOT NULL,
    [ReferencePrice] decimal(19,6) NOT NULL,
    [CreatedUtc] datetime2(7) NOT NULL,
    [Status] nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [LatestFilledQuantity] int NOT NULL,
    [AppliedFilledQuantity] int NOT NULL,
    [LatestNetValue] decimal(19,6) NOT NULL,
    [AppliedNetValue] decimal(19,6) NOT NULL,
    [ReservedQuantity] int NOT NULL,
    [ReservedCash] decimal(19,6) NOT NULL,
    [NeedsWork] bit NOT NULL,
    [LastCheckedUtc] datetime2(7) NULL,
    CONSTRAINT [PK_TradingOrder] PRIMARY KEY ([BindingId],[RequestId]),
    CONSTRAINT [CK_TradingOrder_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingOrder_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId]),
    CONSTRAINT [FK_TradingOrder_Settings] FOREIGN KEY ([BindingId],[PermissionId]) REFERENCES [dbo].[TradingApprovedSettings]([BindingId],[PermissionId]),
    CONSTRAINT [CK_TradingOrder_1] CHECK ([Side] IN (N'BUY',N'SELL') AND [Quantity]>0 AND [ReferencePrice]>=0),
    CONSTRAINT [CK_TradingOrder_2] CHECK ([Status] IN (N'Prepared',N'Submitted',N'Unknown',N'Filled',N'Rejected',N'Cancelled')),
    CONSTRAINT [CK_TradingOrder_3] CHECK ([LatestFilledQuantity]>=0 AND [LatestFilledQuantity]<=[Quantity] AND [AppliedFilledQuantity]>=0 AND [AppliedFilledQuantity]<=[LatestFilledQuantity] AND [AppliedNetValue]>=0 AND [LatestNetValue]>=[AppliedNetValue]),
    CONSTRAINT [CK_TradingOrder_4] CHECK ([ReservedQuantity]>=0 AND [ReservedQuantity]<=[Quantity] AND [ReservedCash]>=0),
    CONSTRAINT [CK_TradingOrder_5] CHECK ([Status]<>N'Filled' OR [LatestFilledQuantity]=[Quantity]),
    CONSTRAINT [CK_TradingOrder_6] CHECK ([Status]<>N'Rejected' OR [LatestFilledQuantity]=0),
    CONSTRAINT [CK_TradingOrder_7] CHECK ([NeedsWork]=1 OR ([Status] IN (N'Filled',N'Rejected',N'Cancelled') AND [AppliedFilledQuantity]=[LatestFilledQuantity] AND [AppliedNetValue]=[LatestNetValue] AND [ReservedQuantity]=0 AND [ReservedCash]=0)),
    CONSTRAINT [CK_TradingOrder_Symbol] CHECK (LEN([Symbol])>0 AND [Symbol]=UPPER([Symbol]))
);
GO

-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingRestrictionCurrent]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [RestrictionId] uniqueidentifier NOT NULL,
    [IsActive] bit NOT NULL,
    [ScopeKey] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ActionCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ReasonCode] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Explanation] nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [EvidenceReference] nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    [LastCheckedUtc] datetime2(7) NULL,
    [NextRetryUtc] datetime2(7) NULL,
    CONSTRAINT [PK_TradingRestrictionCurrent] PRIMARY KEY ([BindingId],[RestrictionId]),
    CONSTRAINT [CK_TradingRestrictionCurrent_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingRestrictionCurrent_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId])
);
GO

-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingCheckpoint]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [Slot] int NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [OperationHash] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [OperationKind] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SavedUtc] datetime2(7) NOT NULL,
    CONSTRAINT [PK_TradingCheckpoint] PRIMARY KEY ([BindingId],[Slot]),
    CONSTRAINT [CK_TradingCheckpoint_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingCheckpoint_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId]),
    CONSTRAINT [CK_TradingCheckpoint_1] CHECK ([Slot] IN (0,1) AND [Slot]=[Version]%2),
    CONSTRAINT [CK_TradingCheckpoint_2] CHECK (LEN([OperationHash])=64 AND [OperationHash] NOT LIKE '%[^0-9A-F]%')
);
GO

CREATE INDEX [IX_TradingPositionCurrent_Active] ON [dbo].[TradingPositionCurrent] ([BindingId],[PositionId]) WHERE [IsOpen]=1;
GO
CREATE INDEX [IX_TradingSymbolCurrent_Active] ON [dbo].[TradingSymbolCurrent] ([BindingId],[Symbol]) WHERE [IsRelevant]=1;
GO
CREATE INDEX [IX_TradingOrder_Active] ON [dbo].[TradingOrder] ([BindingId],[RequestId]) WHERE [NeedsWork]=1;
GO
CREATE INDEX [IX_TradingRestrictionCurrent_Active] ON [dbo].[TradingRestrictionCurrent] ([BindingId],[RestrictionId]) WHERE [IsActive]=1;
GO
CREATE UNIQUE INDEX [UX_TradingOrder_Decision] ON [dbo].[TradingOrder] ([BindingId],[DecisionId]);
GO
CREATE UNIQUE INDEX [UX_TradingOrder_Broker] ON [dbo].[TradingOrder] ([BindingId],[BrokerOrderId]) WHERE [BrokerOrderId] IS NOT NULL;
GO
CREATE UNIQUE INDEX [UX_TradingRestrictionCurrent_Cause] ON [dbo].[TradingRestrictionCurrent] ([BindingId],[ScopeKey],[ActionCode],[ReasonCode]) WHERE [IsActive]=1;
GO

CREATE TRIGGER [dbo].[TradingApprovedSettings_Immutable] ON [dbo].[TradingApprovedSettings]
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted) THROW 54601,'Approved settings are immutable. A replacement requires a new identity.',1;
END;
GO

CREATE TRIGGER [dbo].[TradingOrder_IntentImmutable] ON [dbo].[TradingOrder]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT BindingId,RequestId,DecisionId,PermissionId,PositionId,SecurityId,Symbol,Side,Quantity,ReferencePrice,CreatedUtc FROM deleted
        EXCEPT
        SELECT BindingId,RequestId,DecisionId,PermissionId,PositionId,SecurityId,Symbol,Side,Quantity,ReferencePrice,CreatedUtc FROM inserted)
        THROW 54602,'Original order intent cannot change.',1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.BindingId=d.BindingId AND i.RequestId=d.RequestId
        WHERE (d.BrokerOrderId IS NOT NULL AND (i.BrokerOrderId IS NULL OR d.BrokerOrderId<>i.BrokerOrderId))
        OR i.LatestFilledQuantity<d.LatestFilledQuantity OR i.AppliedFilledQuantity<d.AppliedFilledQuantity
        OR i.LatestNetValue<d.LatestNetValue OR i.AppliedNetValue<d.AppliedNetValue
        OR (d.Status IN (N'Filled',N'Rejected',N'Cancelled') AND d.Status<>i.Status)
        OR (d.NeedsWork=0 AND (i.NeedsWork=1 OR i.LatestFilledQuantity<>d.LatestFilledQuantity
            OR i.AppliedFilledQuantity<>d.AppliedFilledQuantity OR i.LatestNetValue<>d.LatestNetValue
            OR i.AppliedNetValue<>d.AppliedNetValue OR i.ReservedQuantity<>d.ReservedQuantity OR i.ReservedCash<>d.ReservedCash)))
        THROW 54603,'Order outcome regressed or completed accounting was reopened.',1;
END;
GO

COMMIT TRANSACTION;
GO
