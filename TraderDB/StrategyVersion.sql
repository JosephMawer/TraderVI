CREATE TABLE [dbo].[StrategyVersion]
(
	[VersionId]              UNIQUEIDENTIFIER NOT NULL,
	[VersionName]            NVARCHAR(32)     NOT NULL,
	[Description]            NVARCHAR(256)    NULL,
	[IsActive]               BIT              NOT NULL,
	[MinCompositeScore]      FLOAT            NULL,
	[MinDirectionProb]       FLOAT            NULL,
	[RegressionVeto]         FLOAT            NULL,
	[StopLossPercent]        FLOAT            NULL,
	[WarningPercent]         FLOAT            NULL,
	[MaxPositions]           INT              NULL,
	[CreatedUtc]             DATETIME2        NOT NULL,
	[Notes]                  NVARCHAR(MAX)    NULL,
	[MinBreakoutProb]        FLOAT            NULL,
	[MinDirectionEdge]       FLOAT            NULL,
	[MaxDownProb]            FLOAT            NULL,
	[BreadthVetoThreshold]   FLOAT            NULL,
	[StrongBreakoutOverride] FLOAT            NULL,
	[StrongEdgeOverride]     FLOAT            NULL,

	CONSTRAINT [PK_StrategyVersion] PRIMARY KEY CLUSTERED ([VersionId])
);
