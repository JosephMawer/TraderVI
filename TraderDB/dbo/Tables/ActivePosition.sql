CREATE TABLE [dbo].[ActivePosition]
(
	[PositionId]        UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ActivePosition_PositionId] DEFAULT (NEWID()),
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
	[ExecutionMode]     NVARCHAR(8)      NOT NULL CONSTRAINT [DF_ActivePosition_ExecutionMode] DEFAULT (N'Ghost'),
	[AccountLabel]      NVARCHAR(64)     NULL,
	[StopLossPrice]     DECIMAL(18,4)    NULL,
	[WarningPrice]      DECIMAL(18,4)    NULL,
	[IsActive]          BIT              NOT NULL CONSTRAINT [DF_ActivePosition_IsActive] DEFAULT ((1)),
	[LastUpdatedUtc]    DATETIME2        NOT NULL CONSTRAINT [DF_ActivePosition_LastUpdatedUtc] DEFAULT (SYSUTCDATETIME()),
	[Notes]             NVARCHAR(512)    NULL,

	CONSTRAINT [PK_ActivePosition] PRIMARY KEY CLUSTERED ([PositionId]),
	CONSTRAINT [FK_ActivePosition_Pick] FOREIGN KEY ([OriginalPickId]) REFERENCES [dbo].[DailyPick] ([PickId]),
	CONSTRAINT [CK_ActivePosition_ExecutionMode] CHECK ([ExecutionMode] IN (N'Ghost', N'Real')),
	CONSTRAINT [CK_ActivePosition_AccountLabel] CHECK
	(
		([ExecutionMode] = N'Ghost' AND [AccountLabel] IS NULL)
		OR ([ExecutionMode] = N'Real' AND LEN(LTRIM(RTRIM([AccountLabel]))) BETWEEN 1 AND 64)
	)
);
GO

CREATE INDEX [IX_ActivePosition_Symbol]
	ON [dbo].[ActivePosition] ([Symbol]);
GO

CREATE INDEX [IX_ActivePosition_IsActive]
	ON [dbo].[ActivePosition] ([IsActive])
	WHERE [IsActive] = 1;
GO
