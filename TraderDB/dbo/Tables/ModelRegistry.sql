CREATE TABLE [dbo].[ModelRegistry]
(
	[ModelId]        UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ModelRegistry_ModelId] DEFAULT (NEWID()),
	[Name]           NVARCHAR(128)    NOT NULL,
	[TaskType]       NVARCHAR(64)     NOT NULL,
	[ModelKind]      NVARCHAR(32)     NOT NULL,
	[Family]         NVARCHAR(32)     NULL,
	[TimeFrame]      NVARCHAR(16)     NOT NULL,
	[LookbackBars]   INT              NOT NULL,
	[HorizonBars]    INT              NOT NULL CONSTRAINT [DF_ModelRegistry_HorizonBars] DEFAULT ((0)),
	[InputSchema]    NVARCHAR(64)     NOT NULL,
	[FeatureSet]     NVARCHAR(64)     NULL,
	[ZipPath]        NVARCHAR(260)    NOT NULL,
	[ThresholdBuy]   FLOAT            NOT NULL CONSTRAINT [DF_ModelRegistry_ThresholdBuy] DEFAULT ((0.60)),
	[ThresholdSell]  FLOAT            NOT NULL CONSTRAINT [DF_ModelRegistry_ThresholdSell] DEFAULT ((0.40)),
	[IsEnabled]      BIT              NOT NULL CONSTRAINT [DF_ModelRegistry_IsEnabled] DEFAULT ((1)),
	[TrainedFromUtc] DATETIME2        NULL,
	[TrainedToUtc]   DATETIME2        NULL,
	[CreatedUtc]     DATETIME2        NOT NULL CONSTRAINT [DF_ModelRegistry_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
	[Notes]          NVARCHAR(4000)   NULL,

	CONSTRAINT [PK_ModelRegistry] PRIMARY KEY CLUSTERED ([ModelId])
);
GO

CREATE INDEX [IX_ModelRegistry_Enabled]
	ON [dbo].[ModelRegistry] ([IsEnabled], [TimeFrame], [TaskType]);
GO
