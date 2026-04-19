CREATE TABLE [dbo].[StockSectorMap]
(
	[Symbol]           NVARCHAR(10)  NOT NULL,
	[Sector]           NVARCHAR(50)  NOT NULL,
	[Industry]         NVARCHAR(100) NULL,
	[SectorIndexSymbol] NVARCHAR(10) NULL,
	[LastUpdated]      DATETIME2     NOT NULL,

	CONSTRAINT [PK_StockSectorMap] PRIMARY KEY CLUSTERED ([Symbol])
);
