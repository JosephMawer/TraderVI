CREATE TABLE [dbo].[EngineCurrentState]
(
    [Kind] NVARCHAR(32) NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [SourceHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [StateHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [StateJson] NVARCHAR(MAX) NOT NULL,
    [UpdatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineCurrentState_Updated] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineCurrentState] PRIMARY KEY ([Kind],[OwnerId]),
    CONSTRAINT [CK_EngineCurrentState_Revision] CHECK ([Revision]>=0),
    CONSTRAINT [CK_EngineCurrentState_Json] CHECK (ISJSON([StateJson])=1),
    CONSTRAINT [CK_EngineCurrentState_Hashes] CHECK (LEN([SourceHash])=64 AND LEN([StateHash])=64
        AND [SourceHash] NOT LIKE '%[^0-9A-F]%' AND [StateHash] NOT LIKE '%[^0-9A-F]%')
);
GO
