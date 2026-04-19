CREATE TABLE [dbo].[RelativeStrengthFeatures]
(
	[Symbol]                NVARCHAR(20) NOT NULL,
	[Date]                  DATE         NOT NULL,
	[SectorIndexSymbol]     NVARCHAR(10) NOT NULL,
	[RS_StockVsSector_5d]   FLOAT        NULL,
	[RS_StockVsSector_10d]  FLOAT        NULL,
	[RS_StockVsSector_20d]  FLOAT        NULL,
	[RS_StockVsSector_60d]  FLOAT        NULL,
	[RS_StockVsMarket_5d]   FLOAT        NULL,
	[RS_StockVsMarket_10d]  FLOAT        NULL,
	[RS_StockVsMarket_20d]  FLOAT        NULL,
	[RS_StockVsMarket_60d]  FLOAT        NULL,
	[RS_SectorVsMarket_5d]  FLOAT        NULL,
	[RS_SectorVsMarket_10d] FLOAT        NULL,
	[RS_SectorVsMarket_20d] FLOAT        NULL,
	[RS_SectorVsMarket_60d] FLOAT        NULL,
	[RS_Z_StockVsSector]    FLOAT        NULL,
	[RS_Z_StockVsMarket]    FLOAT        NULL,
	[RS_Z_SectorVsMarket]   FLOAT        NULL,
	[CompositeScore]        FLOAT        NULL,

	CONSTRAINT [PK_RelativeStrengthFeatures] PRIMARY KEY CLUSTERED ([Symbol], [Date])
);
