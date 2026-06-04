CREATE TABLE [dbo].[Symbols]
(
	[Symbol]                   NVARCHAR(20)  NOT NULL,
	[LongName]                 NVARCHAR(200) NULL,
	[ShortName]                NVARCHAR(100) NULL,
	[Sector]                   NVARCHAR(100) NULL,
	[Industry]                 NVARCHAR(100) NULL,
	[ExchangeCode]             NVARCHAR(20)  NULL,
	[IsActive]                 BIT           NOT NULL,
	[CreatedUtc]               DATETIME2     NOT NULL,
	[SecurityType]             NVARCHAR(20)  NOT NULL,
	-- ADR-0009: true when the row represents a leveraged or inverse ETP
	-- (e.g. BetaPro 2x/-2x, MegaLong/MegaShort 3x, SavvyLong/SavvyShort,
	-- LFG Daily 2x). Source data often classifies these as 'Stock', but
	-- Delphi excludes them from the ranking universe via this flag because
	-- their daily-reset path dependency violates the ML training
	-- distribution. SymbolsRepository.GetEquitiesAsync filters on
	-- IsLeveragedOrInverseEtp = 0.
	[IsLeveragedOrInverseEtp]  BIT           NOT NULL CONSTRAINT [DF_Symbols_IsLeveragedOrInverseEtp] DEFAULT (0),

	CONSTRAINT [PK_Symbols] PRIMARY KEY CLUSTERED ([Symbol])
);
