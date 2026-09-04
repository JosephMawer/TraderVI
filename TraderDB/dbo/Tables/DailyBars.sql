CREATE TABLE [dbo].[DailyBars]
(
	[Id]        INT IDENTITY (1, 1) NOT NULL,
	[Symbol]    VARCHAR(10)  NOT NULL,
	[Date]      DATE         NOT NULL,
	[Open]      REAL         NOT NULL,
	[High]      REAL         NOT NULL,
	[Low]       REAL         NOT NULL,
	[Close]     REAL         NOT NULL,
	[Volume]    BIGINT       NOT NULL,
	[CreatedAt] DATETIME2    NULL CONSTRAINT [DF_DailyBars_CreatedAt] DEFAULT (GETUTCDATE()),

	CONSTRAINT [PK_DailyBars] PRIMARY KEY CLUSTERED ([Id]),
	CONSTRAINT [UQ_DailyBars_Symbol_Date] UNIQUE ([Symbol], [Date])
);
GO

CREATE INDEX [idx_symbol_date]
	ON [dbo].[DailyBars] ([Symbol], [Date]);
GO
