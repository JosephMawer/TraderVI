CREATE TABLE [dbo].[ShadowPortfolioGeneration]
(
    [GenerationId]           UNIQUEIDENTIFIER NOT NULL,
    [PolicyVersion]          NVARCHAR(32)     NOT NULL,
    [Status]                 NVARCHAR(32)     NOT NULL,
    [TotalAccountValue]      DECIMAL(19,6)    NOT NULL,
    [AvailableAccountCash]   DECIMAL(19,6)    NOT NULL,
    [RealSnapshotUtc]        DATETIME2        NOT NULL,
    [ActivatedUtc]           DATETIME2        NULL,
    [StoppedUtc]             DATETIME2        NULL,
    [CreatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolioGeneration_CreatedUtc] DEFAULT (SYSUTCDATETIME()),
    [UpdatedUtc]             DATETIME2        NOT NULL CONSTRAINT [DF_ShadowPortfolioGeneration_UpdatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_ShadowPortfolioGeneration] PRIMARY KEY CLUSTERED ([GenerationId]),
    CONSTRAINT [CK_ShadowPortfolioGeneration_Status] CHECK ([Status] IN (N'Draft', N'Active', N'Paused', N'CapitalReviewRequired', N'Stopped')),
    CONSTRAINT [CK_ShadowPortfolioGeneration_Capital] CHECK
    (
        [TotalAccountValue] > 0
        AND [AvailableAccountCash] >= 0
        AND [AvailableAccountCash] <= [TotalAccountValue]
    ),
    CONSTRAINT [CK_ShadowPortfolioGeneration_Lifecycle] CHECK
    (
        ([Status] = N'Draft' AND [ActivatedUtc] IS NULL AND [StoppedUtc] IS NULL)
        OR ([Status] IN (N'Active', N'Paused', N'CapitalReviewRequired') AND [ActivatedUtc] IS NOT NULL AND [StoppedUtc] IS NULL)
        OR ([Status] = N'Stopped' AND [ActivatedUtc] IS NOT NULL AND [StoppedUtc] IS NOT NULL)
    )
);
GO

