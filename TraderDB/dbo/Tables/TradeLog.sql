CREATE TABLE [dbo].[TradeLog]
(
	[TradeId]            UNIQUEIDENTIFIER NOT NULL,
	[Symbol]             NVARCHAR(16)     NOT NULL,
	[TradeType]          NVARCHAR(8)      NOT NULL,
	[TradeDate]          DATETIME2        NOT NULL,
	[Shares]             INT              NOT NULL,
	[Price]              DECIMAL(18,4)    NOT NULL,
	[Amount]             DECIMAL(18,2)    NOT NULL,
	[Commission]         DECIMAL(18,2)    NULL,
	[NetAmount]          DECIMAL(18,2)    NOT NULL,
	[PositionId]         UNIQUEIDENTIFIER NULL,
	[Reason]             NVARCHAR(64)     NULL,
	[RealizedPnL]        DECIMAL(18,2)    NULL,
	[RealizedPnLPct]     FLOAT            NULL,
	[HoldingDays]        INT              NULL,
	[EntryComposite]     FLOAT            NULL,
	[ExitComposite]      FLOAT            NULL,
	[StrategyVersionId]  UNIQUEIDENTIFIER NULL,
	[CreatedUtc]         DATETIME2        NOT NULL,
	[Notes]              NVARCHAR(512)    NULL,

	CONSTRAINT [PK_TradeLog] PRIMARY KEY CLUSTERED ([TradeId])
);
