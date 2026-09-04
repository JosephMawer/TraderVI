CREATE TABLE [dbo].[StrategyVersion]
(
	[VersionId]              UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_StrategyVersion_VersionId] DEFAULT (NEWID()),
	[VersionName]            NVARCHAR(32)     NOT NULL,
	[Description]            NVARCHAR(256)    NULL,
	[IsActive]               BIT              NOT NULL CONSTRAINT [DF_StrategyVersion_IsActive] DEFAULT ((0)),
	[MinCompositeScore]      FLOAT            NULL CONSTRAINT [DF_StrategyVersion_MinCompositeScore] DEFAULT ((0.35)),
	[MinDirectionProb]       FLOAT            NULL CONSTRAINT [DF_StrategyVersion_MinDirectionProb] DEFAULT ((0.25)),
	[RegressionVeto]         FLOAT            NULL CONSTRAINT [DF_StrategyVersion_RegressionVeto] DEFAULT ((-0.03)),
	[StopLossPercent]        FLOAT            NULL CONSTRAINT [DF_StrategyVersion_StopLossPercent] DEFAULT ((-0.10)),
	[WarningPercent]         FLOAT            NULL CONSTRAINT [DF_StrategyVersion_WarningPercent] DEFAULT ((-0.05)),
	[MaxPositions]           INT              NULL CONSTRAINT [DF_StrategyVersion_MaxPositions] DEFAULT ((1)),
	[CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_StrategyVersion_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
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
	CONSTRAINT [UQ_StrategyVersion_Name] UNIQUE ([VersionName]),
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
