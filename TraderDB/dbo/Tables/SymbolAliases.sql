CREATE TABLE [dbo].[SymbolAliases]
(
    [Source] NVARCHAR(32) NOT NULL,
    [Alias] NVARCHAR(20) NOT NULL,
    [Symbol] NVARCHAR(20) NOT NULL,
    [ExchangeMic] NVARCHAR(4) NOT NULL,
    [VerifiedUtc] DATETIME2(7) NOT NULL,
    CONSTRAINT [PK_SymbolAliases] PRIMARY KEY ([Source], [Alias]),
    CONSTRAINT [FK_SymbolAliases_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols]([Symbol]),
    CONSTRAINT [CK_SymbolAliases_Content] CHECK
        (LEN(LTRIM(RTRIM([Source]))) > 0 AND LEN(LTRIM(RTRIM([Alias]))) > 0
         AND [Alias] <> [Symbol] AND LEN([ExchangeMic]) = 4)
);
