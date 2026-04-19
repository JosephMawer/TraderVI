CREATE TABLE [dbo].[ModelRegistry]
(
	[ModelId]        UNIQUEIDENTIFIER NOT NULL,
	[Name]           NVARCHAR(128)    NOT NULL,
	[TaskType]       NVARCHAR(64)     NOT NULL,
	[ModelKind]      NVARCHAR(32)     NOT NULL,
	[Family]         NVARCHAR(32)     NULL,
	[TimeFrame]      NVARCHAR(16)     NOT NULL,
	[LookbackBars]   INT              NOT NULL,
	[HorizonBars]    INT              NOT NULL,
	[InputSchema]    NVARCHAR(64)     NOT NULL,
	[FeatureSet]     NVARCHAR(64)     NULL,
	[ZipPath]        NVARCHAR(260)    NOT NULL,
	[ThresholdBuy]   FLOAT            NOT NULL,
	[ThresholdSell]  FLOAT            NOT NULL,
	[IsEnabled]      BIT              NOT NULL,
	[TrainedFromUtc] DATETIME2        NULL,
	[TrainedToUtc]   DATETIME2        NULL,
	[CreatedUtc]     DATETIME2        NOT NULL,
	[Notes]          NVARCHAR(4000)   NULL,

	CONSTRAINT [PK_ModelRegistry] PRIMARY KEY CLUSTERED ([ModelId])
);
