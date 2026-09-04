CREATE TABLE [dbo].[TradeLog]
(
	[TradeId]            UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_TradeLog_TradeId] DEFAULT (NEWID()),
	[Symbol]             NVARCHAR(16)     NOT NULL,
	[TradeType]          NVARCHAR(8)      NOT NULL,
	[TradeDate]          DATETIME2        NOT NULL,
	[Shares]             INT              NOT NULL,
	[Price]              DECIMAL(18,4)    NOT NULL,
	[Amount]             DECIMAL(18,2)    NOT NULL,
	[Commission]         DECIMAL(18,2)    NULL CONSTRAINT [DF_TradeLog_Commission] DEFAULT ((0)),
	[NetAmount]          DECIMAL(18,2)    NOT NULL,
	[PositionId]         UNIQUEIDENTIFIER NULL,
	[ExecutionMode]      NVARCHAR(8)      NOT NULL CONSTRAINT [DF_TradeLog_ExecutionMode] DEFAULT (N'Ghost'),
	[AccountLabel]       NVARCHAR(64)     NULL,
	[Reason]             NVARCHAR(64)     NULL,
	[RealizedPnL]        DECIMAL(18,2)    NULL,
	[RealizedPnLPct]     FLOAT            NULL,
	[HoldingDays]        INT              NULL,
	[EntryComposite]     FLOAT            NULL,
	[ExitComposite]      FLOAT            NULL,
	[StrategyVersionId]  UNIQUEIDENTIFIER NULL,
	[CreatedUtc]         DATETIME2        NOT NULL CONSTRAINT [DF_TradeLog_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
	[Notes]              NVARCHAR(512)    NULL,

	CONSTRAINT [PK_TradeLog] PRIMARY KEY CLUSTERED ([TradeId]),
	CONSTRAINT [FK_TradeLog_Position] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[ActivePosition] ([PositionId]),
	CONSTRAINT [FK_TradeLog_StrategyVersion] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
	CONSTRAINT [CK_TradeLog_TradeType] CHECK ([TradeType] IN (N'BUY', N'SELL')),
	CONSTRAINT [CK_TradeLog_ExecutionMode] CHECK ([ExecutionMode] IN (N'Ghost', N'Real')),
	CONSTRAINT [CK_TradeLog_AccountLabel] CHECK
	(
		([ExecutionMode] = N'Ghost' AND [AccountLabel] IS NULL)
		OR ([ExecutionMode] = N'Real' AND LEN(LTRIM(RTRIM([AccountLabel]))) BETWEEN 1 AND 64)
	)
);
GO

CREATE INDEX [IX_TradeLog_Symbol]
	ON [dbo].[TradeLog] ([Symbol]);
GO

CREATE INDEX [IX_TradeLog_TradeDate]
	ON [dbo].[TradeLog] ([TradeDate] DESC);
GO

CREATE INDEX [IX_TradeLog_TradeType]
	ON [dbo].[TradeLog] ([TradeType]);
GO
