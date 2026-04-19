CREATE TABLE [dbo].[Quotes]
(
	[Symbol]        NVARCHAR(20)  NOT NULL,
	[CreatedUtc]    DATETIME2     NOT NULL,
	[Price]         DECIMAL(18,4) NULL,
	[PriceChange]   DECIMAL(18,4) NULL,
	[PercentChange] DECIMAL(18,4) NULL,
	[DayHigh]       DECIMAL(18,4) NULL,
	[DayLow]        DECIMAL(18,4) NULL,
	[PrevClose]     DECIMAL(18,4) NULL,
	[OpenPrice]     DECIMAL(18,4) NULL,
	[Bid]           DECIMAL(18,4) NULL,
	[Ask]           DECIMAL(18,4) NULL,
	[Weeks52High]   DECIMAL(18,4) NULL,
	[Weeks52Low]    DECIMAL(18,4) NULL,
	[Volume]        BIGINT        NULL,
	[IngestedUtc]   DATETIME2     NOT NULL,

	CONSTRAINT [PK_Quotes] PRIMARY KEY CLUSTERED ([Symbol], [CreatedUtc])
);
