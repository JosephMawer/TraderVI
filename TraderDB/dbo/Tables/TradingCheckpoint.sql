-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingCheckpoint]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [Slot] int NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [OperationHash] varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [OperationKind] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SavedUtc] datetime2(7) NOT NULL,
    CONSTRAINT [PK_TradingCheckpoint] PRIMARY KEY ([BindingId],[Slot]),
    CONSTRAINT [CK_TradingCheckpoint_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingCheckpoint_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId]),
    CONSTRAINT [CK_TradingCheckpoint_1] CHECK ([Slot] IN (0,1) AND [Slot]=[Version]%2),
    CONSTRAINT [CK_TradingCheckpoint_2] CHECK (LEN([OperationHash])=64 AND [OperationHash] NOT LIKE '%[^0-9A-F]%')
);
GO
