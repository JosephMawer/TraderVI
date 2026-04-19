CREATE TABLE [dbo].[DailyPick]
(
	[PickId]             UNIQUEIDENTIFIER NOT NULL,
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
	[CreatedUtc]         DATETIME2        NOT NULL,
	[Notes]              NVARCHAR(512)    NULL,

	CONSTRAINT [PK_DailyPick] PRIMARY KEY CLUSTERED ([PickId])
);
