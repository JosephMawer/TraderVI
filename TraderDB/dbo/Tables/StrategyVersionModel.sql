CREATE TABLE [dbo].[StrategyVersionModel]
(
	[VersionId]       UNIQUEIDENTIFIER NOT NULL,
	[ModelId]         UNIQUEIDENTIFIER NOT NULL,
	[CompositeWeight] FLOAT            NULL CONSTRAINT [DF_StrategyVersionModel_CompositeWeight] DEFAULT ((0)),
	[IsRequired]      BIT              NOT NULL CONSTRAINT [DF_StrategyVersionModel_IsRequired] DEFAULT ((0)),
	[Role]            NVARCHAR(32)     NULL,

	CONSTRAINT [PK_StrategyVersionModel] PRIMARY KEY CLUSTERED ([VersionId], [ModelId]),
	CONSTRAINT [FK_StrategyVersionModel_Version] FOREIGN KEY ([VersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]) ON DELETE CASCADE,
	CONSTRAINT [FK_StrategyVersionModel_Model]   FOREIGN KEY ([ModelId])   REFERENCES [dbo].[ModelRegistry] ([ModelId]) ON DELETE CASCADE
);
