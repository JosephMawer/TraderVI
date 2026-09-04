CREATE TABLE [dbo].[DailyPick]
(
	[PickId]             UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_DailyPick_PickId] DEFAULT (NEWID()),
	[PickDate]           DATE             NOT NULL,
	[Symbol]             NVARCHAR(16)     NOT NULL,
	[Rank]               INT              NOT NULL,
	[Direction]          NVARCHAR(8)      NOT NULL,
	[CompositeScore]     FLOAT            NOT NULL,
	[BreakoutProb]       FLOAT            NULL,
	[DirectionProb]      FLOAT            NULL,
	[VolExpansionProb]   FLOAT            NULL,
	[RelStrengthProb]    FLOAT            NULL,
	[ExpectedReturn]     FLOAT            NULL,
	[SuggestedSize]      DECIMAL(18,2)    NULL,
	[AllocationPercent]  FLOAT            NULL,
	[StrategyVersionId]  UNIQUEIDENTIFIER NULL,
	[CreatedUtc]         DATETIME2        NOT NULL CONSTRAINT [DF_DailyPick_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
	[Notes]              NVARCHAR(512)    NULL,
	[Lens]               NVARCHAR(16)     NOT NULL CONSTRAINT [DF_DailyPick_Lens] DEFAULT ('Breakout'),

	CONSTRAINT [PK_DailyPick] PRIMARY KEY CLUSTERED ([PickId]),
	CONSTRAINT [FK_DailyPick_StrategyVersion] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId])

);
GO

CREATE INDEX [IX_DailyPick_Date]
	ON [dbo].[DailyPick] ([PickDate] DESC);
GO

CREATE INDEX [IX_DailyPick_Symbol]
	ON [dbo].[DailyPick] ([Symbol]);
GO
