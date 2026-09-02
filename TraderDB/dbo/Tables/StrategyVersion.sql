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
	[InitialCodeCommit]      NVARCHAR(128)    NULL,
	[DecisionRef]            NVARCHAR(64)     NULL,
	[MinBreakoutProb]        FLOAT            NULL,
	[MinDirectionEdge]       FLOAT            NULL,
	[MaxDownProb]            FLOAT            NULL,
	[BreadthVetoThreshold]   FLOAT            NULL,
	[StrongBreakoutOverride] FLOAT            NULL,
	[StrongEdgeOverride]     FLOAT            NULL,

	CONSTRAINT [PK_StrategyVersion] PRIMARY KEY CLUSTERED ([VersionId]),
	CONSTRAINT [CK_StrategyVersion_CodeIdentity] CHECK
	(
		([InitialCodeCommit] IS NULL AND [DecisionRef] IS NULL)
		OR
		(
			[InitialCodeCommit] IS NOT NULL
			AND [DecisionRef] IS NOT NULL
			AND LEN(LTRIM(RTRIM([InitialCodeCommit]))) BETWEEN 7 AND 128
			AND LEN(LTRIM(RTRIM([DecisionRef]))) BETWEEN 1 AND 64
		)
	)
);
