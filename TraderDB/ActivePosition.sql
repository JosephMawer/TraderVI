CREATE TABLE [dbo].[ActivePosition]
(
	[PositionId]        UNIQUEIDENTIFIER NOT NULL,
	[Symbol]            NVARCHAR(16)     NOT NULL,
	[EntryDate]         DATE             NOT NULL,
	[EntryPrice]        DECIMAL(18,4)    NOT NULL,
	[Shares]            INT              NOT NULL,
	[CostBasis]         DECIMAL(18,2)    NOT NULL,
	[CurrentPrice]      DECIMAL(18,4)    NULL,
	[CurrentValue]      DECIMAL(18,2)    NULL,
	[UnrealizedPnL]     DECIMAL(18,2)    NULL,
	[UnrealizedPnLPct]  FLOAT            NULL,
	[HighWaterMark]     DECIMAL(18,4)    NULL,
	[DrawdownFromHigh]  FLOAT            NULL,
	[DaysHeld]          INT              NULL,
	[OriginalPickId]    UNIQUEIDENTIFIER NULL,
	[StopLossPrice]     DECIMAL(18,4)    NULL,
	[WarningPrice]      DECIMAL(18,4)    NULL,
	[IsActive]          BIT              NOT NULL,
	[LastUpdatedUtc]    DATETIME2        NOT NULL,
	[Notes]             NVARCHAR(512)    NULL,

	CONSTRAINT [PK_ActivePosition] PRIMARY KEY CLUSTERED ([PositionId])
);
