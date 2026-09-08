CREATE TABLE [dbo].[TradingStrategyVersion]
(
 [VersionId] uniqueidentifier NOT NULL,
 [StrategyId] uniqueidentifier NOT NULL,
 [DailyStrategyVersionId] uniqueidentifier NOT NULL,
 [Family] nvarchar(24) NOT NULL,
 [Fingerprint] varchar(64) NOT NULL,
 [DefinitionJson] nvarchar(max) NOT NULL,
 [CreatedUtc] datetime2 NOT NULL CONSTRAINT [DF_TradingStrategyVersion_Created] DEFAULT SYSUTCDATETIME(),
 CONSTRAINT [PK_TradingStrategyVersion] PRIMARY KEY ([VersionId]),
 CONSTRAINT [FK_TradingStrategyVersion_Daily] FOREIGN KEY ([DailyStrategyVersionId]) REFERENCES [dbo].[StrategyVersion]([VersionId]),
 CONSTRAINT [UQ_TradingStrategyVersion_Fingerprint] UNIQUE ([Fingerprint]),
 CONSTRAINT [CK_TradingStrategyVersion_Family] CHECK ([Family] IN(N'DailyShadow',N'DelphiLive')),
 CONSTRAINT [CK_TradingStrategyVersion_Json] CHECK (ISJSON([DefinitionJson])=1 AND LEN([Fingerprint])=64)
);
GO
