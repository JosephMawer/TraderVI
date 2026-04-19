CREATE TABLE [dbo].[StrategyVersionModel]
(
	[VersionId]       UNIQUEIDENTIFIER NOT NULL,
	[ModelId]         UNIQUEIDENTIFIER NOT NULL,
	[CompositeWeight] FLOAT            NULL,
	[IsRequired]      BIT              NOT NULL,
	[Role]            NVARCHAR(32)     NULL,

	CONSTRAINT [PK_StrategyVersionModel] PRIMARY KEY CLUSTERED ([VersionId], [ModelId]),
	CONSTRAINT [FK_StrategyVersionModel_Version] FOREIGN KEY ([VersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
	CONSTRAINT [FK_StrategyVersionModel_Model]   FOREIGN KEY ([ModelId])   REFERENCES [dbo].[ModelRegistry] ([ModelId])
);
