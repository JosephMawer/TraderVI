CREATE TABLE [dbo].[Symbols]
(
	[Symbol]       NVARCHAR(20)  NOT NULL,
	[LongName]     NVARCHAR(200) NULL,
	[ShortName]    NVARCHAR(100) NULL,
	[Sector]       NVARCHAR(100) NULL,
	[Industry]     NVARCHAR(100) NULL,
	[ExchangeCode] NVARCHAR(20)  NULL,
	[IsActive]     BIT           NOT NULL,
	[CreatedUtc]   DATETIME2     NOT NULL,
	[SecurityType] NVARCHAR(20)  NOT NULL,

	CONSTRAINT [PK_Symbols] PRIMARY KEY CLUSTERED ([Symbol])
);
