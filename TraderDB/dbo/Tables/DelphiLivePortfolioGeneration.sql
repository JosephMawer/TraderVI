CREATE TABLE [dbo].[DelphiLivePortfolioGeneration]
(
    [GenerationId] UNIQUEIDENTIFIER NOT NULL,
    [AssignmentId] UNIQUEIDENTIFIER NOT NULL,
    [DelphiLivePolicyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [PortfolioRole] NVARCHAR(32) NOT NULL,
    [ExperimentId] UNIQUEIDENTIFIER NULL,
    [StartingCapital] DECIMAL(28, 6) NOT NULL,
    [Currency] CHAR(3) NOT NULL,
    [EffectiveTradingDate] DATE NOT NULL,
    [EndExclusiveTradingDate] DATE NULL,
    [EffectiveSessionOpenUtc] DATETIME2 NOT NULL,
    [AuthorizedUtc] DATETIME2 NOT NULL,
    [AuthorizedBy] NVARCHAR(128) NOT NULL,
    [AuthorizationReason] NVARCHAR(1024) NOT NULL,
    [AuthorizationJson] NVARCHAR(MAX) NOT NULL,
    [EngineeringTrialId] UNIQUEIDENTIFIER NULL,
    [ComparisonBatchId] UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_DelphiLivePortfolioGeneration] PRIMARY KEY ([GenerationId]),
    CONSTRAINT [FK_DelphiLivePortfolioGeneration_Assignment] FOREIGN KEY ([AssignmentId]) REFERENCES [dbo].[DelphiLivePolicyAssignment] ([AssignmentId]),
    CONSTRAINT FK_DelphiLivePortfolioGeneration_Comparison FOREIGN KEY(ComparisonBatchId) REFERENCES dbo.PaperComparisonBatch(BatchId),
    CONSTRAINT [FK_DelphiLivePortfolioGeneration_Policy] FOREIGN KEY ([DelphiLivePolicyVersionId]) REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId]),
    CONSTRAINT [FK_DelphiLivePortfolioGeneration_Trial] FOREIGN KEY ([EngineeringTrialId], [EffectiveTradingDate]) REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId], [TradingDate]),
    CONSTRAINT [CK_DelphiLivePortfolioGeneration_Capital] CHECK ([StartingCapital] > 0 AND [Currency] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'),
    CONSTRAINT [CK_DelphiLivePortfolioGeneration_End] CHECK ([EndExclusiveTradingDate] IS NULL OR [EndExclusiveTradingDate] > [EffectiveTradingDate]),
    CONSTRAINT [CK_DelphiLivePortfolioGeneration_Authorization] CHECK
        ((([EngineeringTrialId] IS NULL AND [AuthorizedUtc] < [EffectiveSessionOpenUtc]) OR
          ([EngineeringTrialId] IS NOT NULL AND [EngineeringTrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908' AND [EffectiveTradingDate] = '20260908'
           AND [EffectiveSessionOpenUtc] = '2026-09-08T13:30:00' AND [AuthorizedUtc] >= '2026-09-08T17:50:00'
           AND [AuthorizedUtc] < '2026-09-08T19:15:00' AND [StartingCapital] = 1000 AND [Currency] = 'CAD'))
         AND LEN(LTRIM(RTRIM([AuthorizedBy]))) > 0 AND LEN(LTRIM(RTRIM([AuthorizationReason]))) > 0 AND ISJSON([AuthorizationJson]) = 1),
    CONSTRAINT [CK_DelphiLivePortfolioGeneration_Role] CHECK
        (([PortfolioRole] = N'OperationalChampion' AND [ExperimentId] IS NULL AND ComparisonBatchId IS NULL) OR
         ([PortfolioRole] IN (N'ActiveShadowChallenger', N'ShadowBaseline', N'ChampionControl') AND [ExperimentId] IS NOT NULL AND ComparisonBatchId IS NULL) OR
         ([PortfolioRole] = N'GateComparison' AND [ExperimentId] IS NULL AND ComparisonBatchId IS NOT NULL))
);
GO
