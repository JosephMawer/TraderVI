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
