CREATE TABLE [dbo].[EngineEvidenceContent]
(
    [EvidenceSequence] BIGINT IDENTITY(1,1) NOT NULL,
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineEvidenceContent_Recorded] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineEvidenceContent] PRIMARY KEY ([ContentHash]),
    CONSTRAINT [UQ_EngineEvidenceContent_Sequence] UNIQUE ([EvidenceSequence]),
    CONSTRAINT [CK_EngineEvidenceContent_Hash] CHECK (LEN([ContentHash])=64 AND [ContentHash] NOT LIKE '%[^0-9A-F]%'),
    CONSTRAINT [CK_EngineEvidenceContent_Json] CHECK (ISJSON([ContentJson])=1)
);
GO
