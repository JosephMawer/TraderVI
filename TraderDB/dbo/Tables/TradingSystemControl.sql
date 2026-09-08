CREATE TABLE [dbo].[TradingSystemControl]
(
    [SystemKey] NVARCHAR(32) NOT NULL CONSTRAINT [PK_TradingSystemControl] PRIMARY KEY,
    [Paused] BIT NOT NULL,
    [ChangedUtc] DATETIME2 NOT NULL,
    [Reason] NVARCHAR(512) NOT NULL,
    CONSTRAINT [CK_TradingSystemControl_Key] CHECK ([SystemKey] IN (N'Daily',N'DelphiLive',N'SystemShadow',N'TrackedPositions'))
);
