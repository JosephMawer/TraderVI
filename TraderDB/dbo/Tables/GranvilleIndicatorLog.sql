CREATE TABLE [dbo].[GranvilleIndicatorLog]
(
	[LogId]                UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_GranvilleIndicatorLog_LogId] DEFAULT (NEWID()),
	[EvalDate]             DATE             NOT NULL,
	[IndicatorNumber]      INT              NOT NULL,
	[Category]             NVARCHAR(50)     NOT NULL,
	[Name]                 NVARCHAR(128)    NOT NULL,
	[Signal]               NVARCHAR(20)     NOT NULL,
	[GranvillePoints]      INT              NOT NULL,
	[Description]          NVARCHAR(512)    NOT NULL,
	[NetPoints]            INT              NOT NULL,
	[CompositeAdjustment]  FLOAT            NOT NULL,
	[CreatedUtc]           DATETIME2        NOT NULL CONSTRAINT [DF_GranvilleIndicatorLog_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

	CONSTRAINT [PK_GranvilleIndicatorLog] PRIMARY KEY CLUSTERED ([LogId])
);
GO

CREATE INDEX [IX_GranvilleIndicatorLog_EvalDate]
	ON [dbo].[GranvilleIndicatorLog] ([EvalDate]);
GO

CREATE INDEX [IX_GranvilleIndicatorLog_Category]
	ON [dbo].[GranvilleIndicatorLog] ([EvalDate], [Category]);
GO
