CREATE TABLE [dbo].[LlmNarrative]
(
	[NarrativeId]    UNIQUEIDENTIFIER NOT NULL,
	[PickDate]       DATE             NOT NULL,
	[DossierId]      UNIQUEIDENTIFIER NULL,        -- NULL for market-wide summary
	[Scope]          NVARCHAR(16)     NOT NULL,    -- 'PerPick' | 'Market'
	[Symbol]         NVARCHAR(16)     NULL,        -- NULL for market-wide summary
	[Provider]       NVARCHAR(32)     NOT NULL,
	[Model]          NVARCHAR(64)     NOT NULL,
	[Temperature]    FLOAT            NOT NULL,
	[PromptHash]     CHAR(64)         NOT NULL,
	[SystemPrompt]   NVARCHAR(MAX)    NOT NULL,
	[UserPrompt]     NVARCHAR(MAX)    NOT NULL,
	[ResponseText]   NVARCHAR(MAX)    NOT NULL,
	[InputTokens]    INT              NOT NULL,
	[OutputTokens]   INT              NOT NULL,
	[CostUsd]        DECIMAL(18,6)    NOT NULL,
	[LatencyMs]      INT              NOT NULL,
	[SchemaVersion]  INT              NOT NULL,
	[CreatedUtc]     DATETIME2        NOT NULL CONSTRAINT [DF_LlmNarrative_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

	CONSTRAINT [PK_LlmNarrative] PRIMARY KEY CLUSTERED ([NarrativeId]),
	CONSTRAINT [FK_LlmNarrative_DecisionDossier] FOREIGN KEY ([DossierId])
		REFERENCES [dbo].[DecisionDossier] ([DossierId])
);
GO

CREATE INDEX [IX_LlmNarrative_PickDate]
	ON [dbo].[LlmNarrative] ([PickDate], [Scope]);
GO

CREATE INDEX [IX_LlmNarrative_DossierId]
	ON [dbo].[LlmNarrative] ([DossierId]);
GO

CREATE INDEX [IX_LlmNarrative_PromptHash]
	ON [dbo].[LlmNarrative] ([PromptHash]);
GO
