CREATE TABLE [dbo].[BrokerAccountImportBinding]
(
 [BindingId] uniqueidentifier NOT NULL,
 [AccountLabel] nvarchar(64) NOT NULL,
 [BrokerKey] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 [LegacyFingerprint] varchar(64) NOT NULL,
 [CreatedUtc] datetime2 NOT NULL,
 [ReviewedBy] nvarchar(128) NOT NULL,
 CONSTRAINT [PK_BrokerAccountImportBinding] PRIMARY KEY ([BindingId]),
 CONSTRAINT [UQ_BrokerAccountImportBinding_Account] UNIQUE ([AccountLabel]),
 CONSTRAINT [UQ_BrokerAccountImportBinding_Broker] UNIQUE ([BrokerKey]),
 CONSTRAINT [CK_BrokerAccountImportBinding_Identity] CHECK
  ([AccountLabel]=N'TFSA' AND LEN([BrokerKey])=64 AND LEN([LegacyFingerprint])=64 AND LEN(LTRIM(RTRIM([ReviewedBy])))>0)
);
GO
