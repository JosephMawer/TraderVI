CREATE TABLE [dbo].[DelphiLiveSnapshotContent]
(
    [ContentHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ContentJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveSnapshotContent] PRIMARY KEY ([ContentHash]),
    CONSTRAINT [CK_DelphiLiveSnapshotContent_Hash] CHECK (LEN([ContentHash])=64 AND [ContentHash] NOT LIKE '%[^0-9A-F]%'),
    CONSTRAINT [CK_DelphiLiveSnapshotContent_Json] CHECK (ISJSON(N'['+[ContentJson]+N']')=1)
);
GO
