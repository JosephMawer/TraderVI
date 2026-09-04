CREATE TABLE [dbo].[ModelExperiment]
(
	[ExperimentId]      UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ModelExperiment_ExperimentId] DEFAULT (NEWID()),
	[TaskType]          NVARCHAR(64)     NOT NULL,
	[ExperimentName]    NVARCHAR(128)    NOT NULL,
	[LabelDefinition]   NVARCHAR(256)    NULL,
	[FeatureSet]        NVARCHAR(64)     NULL,
	[FeatureCount]      INT              NULL,
	[TrainWindows]      INT              NULL,
	[TestWindows]       INT              NULL,
	[AUC]               FLOAT            NULL,
	[Accuracy]          FLOAT            NULL,
	[F1AtDefault]       FLOAT            NULL,
	[F1AtOptimal]       FLOAT            NULL,
	[OptimalThreshold]  FLOAT            NULL,
	[PrecisionAtOpt]    FLOAT            NULL,
	[RecallAtOpt]       FLOAT            NULL,
	[RMSE]              FLOAT            NULL,
	[MAE]               FLOAT            NULL,
	[RSquared]          FLOAT            NULL,
	[Spearman]          FLOAT            NULL,
	[Hypothesis]        NVARCHAR(512)    NULL,
	[Outcome]           NVARCHAR(512)    NULL,
	[Decision]          NVARCHAR(64)     NULL,
	[CreatedUtc]        DATETIME2        NOT NULL CONSTRAINT [DF_ModelExperiment_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
	[Notes]             NVARCHAR(MAX)    NULL,

	CONSTRAINT [PK_ModelExperiment] PRIMARY KEY CLUSTERED ([ExperimentId])
);
GO

CREATE INDEX [IX_ModelExperiment_CreatedUtc]
	ON [dbo].[ModelExperiment] ([CreatedUtc] DESC);
GO

CREATE INDEX [IX_ModelExperiment_TaskType]
	ON [dbo].[ModelExperiment] ([TaskType]);
GO
