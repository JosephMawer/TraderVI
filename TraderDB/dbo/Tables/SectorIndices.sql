CREATE TABLE [dbo].[SectorIndices]
(
	[Date]          DATE          NOT NULL,
	[Symbol]        NVARCHAR(10)  NOT NULL,
	[SectorName]    NVARCHAR(50)  NOT NULL,
	[Price]         DECIMAL(18,4) NOT NULL,
	[PriceChange]   DECIMAL(18,4) NOT NULL,
	[PercentChange] DECIMAL(18,4) NOT NULL,

	CONSTRAINT [PK_SectorIndices] PRIMARY KEY CLUSTERED ([Date], [Symbol])
);
