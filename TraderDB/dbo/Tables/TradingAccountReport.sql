CREATE TABLE [dbo].[TradingAccountReport]
(
 [ReportId] uniqueidentifier NOT NULL,
 [Sequence] bigint IDENTITY(1,1) NOT NULL,
 [AccountLabel] nvarchar(64) NOT NULL,
 [TradingDate] date NOT NULL,
 [ReportedUtc] datetime2 NOT NULL,
 [ReportedBy] nvarchar(128) NOT NULL,
 [LedgerFingerprint] varchar(64) NOT NULL,
 [RequestHash] varchar(64) NOT NULL,
 [ReportJson] nvarchar(max) NOT NULL,
 CONSTRAINT [PK_TradingAccountReport] PRIMARY KEY ([ReportId]),
 CONSTRAINT [UQ_TradingAccountReport_AccountReport] UNIQUE ([AccountLabel],[ReportId]),
 CONSTRAINT [CK_TradingAccountReport_Json] CHECK (ISJSON([ReportJson])=1 AND LEN([AccountLabel])>0 AND LEN([ReportedBy])>0 AND LEN([LedgerFingerprint])=64 AND LEN([RequestHash])=64)
);
GO
CREATE INDEX [IX_TradingAccountReport_Account] ON [dbo].[TradingAccountReport]([AccountLabel],[Sequence] DESC);
GO
