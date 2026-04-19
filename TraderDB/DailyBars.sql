CREATE TABLE [dbo].[DailyBars]
(
	[Id]        INT          NOT NULL,
	[Symbol]    VARCHAR(10)  NOT NULL,
	[Date]      DATE         NOT NULL,
	[Open]      REAL         NOT NULL,
	[High]      REAL         NOT NULL,
	[Low]       REAL         NOT NULL,
	[Close]     REAL         NOT NULL,
	[Volume]    BIGINT       NOT NULL,
	[CreatedAt] DATETIME2    NULL,

	CONSTRAINT [PK_DailyBars] PRIMARY KEY CLUSTERED ([Id])
);
