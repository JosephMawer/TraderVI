CREATE TABLE [dbo].[DecisionDossier]
(
	[DossierId]      UNIQUEIDENTIFIER NOT NULL,
	[PickDate]       DATE             NOT NULL,
	[PickId]         UNIQUEIDENTIFIER NOT NULL,
	[Symbol]         NVARCHAR(16)     NOT NULL,
	[Rank]           INT              NOT NULL,
	[SchemaVersion]  INT              NOT NULL,
	[DossierJson]    NVARCHAR(MAX)    NOT NULL,
	[CreatedUtc]     DATETIME2        NOT NULL CONSTRAINT [DF_DecisionDossier_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

	CONSTRAINT [PK_DecisionDossier] PRIMARY KEY CLUSTERED ([DossierId]),
	CONSTRAINT [FK_DecisionDossier_DailyPick] FOREIGN KEY ([PickId])
		REFERENCES [dbo].[DailyPick] ([PickId])
);
GO

CREATE INDEX [IX_DecisionDossier_PickDate]
	ON [dbo].[DecisionDossier] ([PickDate], [Rank]);
GO

CREATE INDEX [IX_DecisionDossier_PickId]
	ON [dbo].[DecisionDossier] ([PickId]);
GO
