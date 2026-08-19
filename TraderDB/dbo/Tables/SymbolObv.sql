CREATE TABLE [dbo].[SymbolObv]
(
	[Symbol]    VARCHAR(10)  NOT NULL,
	[Date]      DATE         NOT NULL,
	[Obv]       BIGINT       NOT NULL,   -- Signed running cumulative On-Balance Volume (anchor-relative; only its trend/breakouts are meaningful)
	[CreatedAt] DATETIME2    NULL,

	CONSTRAINT [PK_SymbolObv] PRIMARY KEY CLUSTERED ([Symbol], [Date])
);
