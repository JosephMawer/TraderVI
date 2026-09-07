CREATE TABLE [dbo].[StrategyModelBinding]
(
    [StrategyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [ModelSetId] UNIQUEIDENTIFIER NOT NULL,
    [ModelSetJson] NVARCHAR(MAX) NOT NULL,
    [ModelSetSha256] CHAR(64) NOT NULL,
    [RegisteredUtc] DATETIME2 NOT NULL CONSTRAINT [DF_StrategyModelBinding_RegisteredUtc] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_StrategyModelBinding] PRIMARY KEY ([StrategyVersionId]),
    CONSTRAINT [FK_StrategyModelBinding_Strategy] FOREIGN KEY ([StrategyVersionId]) REFERENCES [dbo].[StrategyVersion] ([VersionId]),
    CONSTRAINT [CK_StrategyModelBinding_Json] CHECK (ISJSON([ModelSetJson]) = 1),
    CONSTRAINT [CK_StrategyModelBinding_Hash] CHECK ([ModelSetSha256] = CONVERT(CHAR(64), HASHBYTES('SHA2_256', [ModelSetJson]), 2))
);
