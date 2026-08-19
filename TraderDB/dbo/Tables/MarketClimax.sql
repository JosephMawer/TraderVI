CREATE TABLE [dbo].[MarketClimax]
(
	[Date]          DATE      NOT NULL,
	[UpBreakouts]   INT       NOT NULL,   -- standing: # XIU-60 names whose latest OBV designation is UP
	[DownBreakouts] INT       NOT NULL,   -- standing: # whose latest OBV designation is DOWN
	[Clx]           INT       NOT NULL,   -- the signal: UpBreakouts - DownBreakouts
	[FreshUp]       INT       NOT NULL,   -- flow diagnostic: # that broke UP on [Date]
	[FreshDown]     INT       NOT NULL,   -- flow diagnostic: # that broke DOWN on [Date]
	[Covered]       INT       NOT NULL,   -- # names with a directional (UP/DOWN) designation
	[BasketSize]    INT       NOT NULL,   -- # names that produced a classifiable OBV series
	[XiuClose]      REAL      NULL,       -- XIU close on [Date], for divergence vs the benchmark
	[CreatedAt]     DATETIME2 NOT NULL CONSTRAINT [DF_MarketClimax_CreatedAt] DEFAULT (SYSUTCDATETIME()),

	CONSTRAINT [PK_MarketClimax] PRIMARY KEY CLUSTERED ([Date])
);
