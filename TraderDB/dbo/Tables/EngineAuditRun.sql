CREATE TABLE [dbo].[EngineAuditRun]
(
    [RunId] UNIQUEIDENTIFIER NOT NULL,
    [Kind] NVARCHAR(16) NOT NULL,
    [BoundaryUtc] DATETIME2 NOT NULL,
    [PlanJson] NVARCHAR(MAX) NOT NULL,
    [PlanHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ProgressJson] NVARCHAR(MAX) NOT NULL,
    [ProgressHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Revision] BIGINT NOT NULL,
    [Status] NVARCHAR(16) NOT NULL,
    [UpdatedUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_EngineAuditRun] PRIMARY KEY ([RunId]),
    CONSTRAINT [CK_EngineAuditRun_State] CHECK ([Kind] IN(N'NewHistory',N'Archive',N'Recovery')
        AND [Status] IN(N'Running',N'Complete',N'Failed') AND [Revision]>=0
        AND ISJSON([PlanJson])=1 AND ISJSON([ProgressJson])=1
        AND LEN([PlanHash])=64 AND [PlanHash] NOT LIKE '%[^0-9A-F]%'
        AND LEN([ProgressHash])=64 AND [ProgressHash] NOT LIKE '%[^0-9A-F]%')
);
GO
