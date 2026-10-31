-- ADR-0117. Prepared compact store; not wired to engine startup.
CREATE TABLE [dbo].[TradingRestrictionCurrent]
(
    [BindingId] uniqueidentifier NOT NULL,
    [Version] bigint NOT NULL,
    [RestrictionId] uniqueidentifier NOT NULL,
    [IsActive] bit NOT NULL,
    [ScopeKey] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ActionCode] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ReasonCode] nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Explanation] nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [EvidenceReference] nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    [LastCheckedUtc] datetime2(7) NULL,
    [NextRetryUtc] datetime2(7) NULL,
    CONSTRAINT [PK_TradingRestrictionCurrent] PRIMARY KEY ([BindingId],[RestrictionId]),
    CONSTRAINT [CK_TradingRestrictionCurrent_Version] CHECK ([Version]>=0),
    CONSTRAINT [FK_TradingRestrictionCurrent_Account] FOREIGN KEY ([BindingId]) REFERENCES [dbo].[TradingAccountCurrent]([BindingId])
);
GO
