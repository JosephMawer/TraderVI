-- Manual migration 028: requires an authorized verified backup. Stop older hosts first.
-- Additive control/audit storage. Does not pause, sell, seed assignments or change account facts.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRANSACTION;
IF DB_NAME()<>N'TraderDB' THROW 51420,'Expected TraderDB.',1;
IF OBJECT_ID(N'dbo.EngineStrategyAssignment',N'U') IS NULL THROW 51421,'Migration 027 is required.',1;
IF OBJECT_ID(N'dbo.TradingSystemControl',N'U') IS NOT NULL THROW 51422,'Migration 028 is already installed.',1;
GO
CREATE TABLE [dbo].[TradingSystemControl]
(
    [SystemKey] NVARCHAR(32) NOT NULL CONSTRAINT [PK_TradingSystemControl] PRIMARY KEY,
    [Paused] BIT NOT NULL,
    [ChangedUtc] DATETIME2 NOT NULL,
    [Reason] NVARCHAR(512) NOT NULL,
    CONSTRAINT [CK_TradingSystemControl_Key] CHECK ([SystemKey] IN (N'Daily',N'DelphiLive',N'SystemShadow',N'TrackedPositions'))
);

GO
CREATE TABLE [dbo].[TradingControlEvent]
(
    [EventId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_TradingControlEvent] PRIMARY KEY,
    [SystemKey] NVARCHAR(32) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL,
    [RecordedBy] NVARCHAR(128) NOT NULL,
    [Paused] BIT NOT NULL,
    [Reason] NVARCHAR(512) NOT NULL,
    [PriorStateJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [CK_TradingControlEvent_Content] CHECK (ISJSON([PriorStateJson])=1 AND LEN(LTRIM(RTRIM([Reason])))>0)
);

GO
CREATE TABLE [dbo].[OperatorExitRequest]
(
    [RequestId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_OperatorExitRequest] PRIMARY KEY,
    [Family] NVARCHAR(32) NOT NULL,
    [TargetId] UNIQUEIDENTIFIER NOT NULL,
    [PositionId] UNIQUEIDENTIFIER NOT NULL,
    [Symbol] NVARCHAR(20) NOT NULL,
    [RequestedUtc] DATETIME2 NOT NULL,
    [RequestedBy] NVARCHAR(128) NOT NULL,
    [Reason] NVARCHAR(512) NOT NULL,
    [PositionJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [UQ_OperatorExitRequest_Position] UNIQUE ([Family],[TargetId],[PositionId]),
    CONSTRAINT [CK_OperatorExitRequest_Content] CHECK
      ([Family] IN (N'DelphiLive',N'SystemShadow',N'TrackedPositions') AND ISJSON([PositionJson])=1 AND LEN(LTRIM(RTRIM([Reason])))>0)
);

GO
CREATE TRIGGER [dbo].[TradingControlEvent_Immutable] ON [dbo].[TradingControlEvent] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51411, 'Trading control history is immutable.', 1;
END;

GO
CREATE TRIGGER [dbo].[OperatorExitRequest_Immutable] ON [dbo].[OperatorExitRequest] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51412, 'Operator exit requests are immutable; preserve the request and its execution history.', 1;
END;

GO
ALTER TABLE dbo.ShadowOrder DROP CONSTRAINT CK_ShadowOrder_Kind;
ALTER TABLE dbo.ShadowOrder ADD CONSTRAINT CK_ShadowOrder_Kind CHECK (OrderKind IN (N'Initial',N'AddOn',N'RiskExit',N'RotationExit',N'SessionTwoExit',N'Reentry',N'OperatorExit'));
COMMIT TRANSACTION;
