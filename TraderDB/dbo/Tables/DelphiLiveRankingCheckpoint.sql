CREATE TABLE [dbo].[DelphiLiveRankingCheckpoint]
(
    [CheckpointId] UNIQUEIDENTIFIER NOT NULL,
    [SessionId] UNIQUEIDENTIFIER NOT NULL,
    [TradingDate] DATE NOT NULL,
    [BarEndUtc] DATETIME2 NOT NULL,
    [Lens] NVARCHAR(16) NOT NULL,
    [ChampionPolicyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [CheckpointJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveRankingCheckpoint] PRIMARY KEY ([CheckpointId]),
    CONSTRAINT [UQ_DelphiLiveRankingCheckpoint_Identity] UNIQUE ([SessionId], [BarEndUtc], [Lens]),
    CONSTRAINT [FK_DelphiLiveRankingCheckpoint_Session] FOREIGN KEY ([SessionId]) REFERENCES [dbo].[DelphiLiveSession] ([SessionId]),
    CONSTRAINT [FK_DelphiLiveRankingCheckpoint_Policy] FOREIGN KEY ([ChampionPolicyVersionId]) REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId]),
    CONSTRAINT [CK_DelphiLiveRankingCheckpoint_Json] CHECK (ISJSON([CheckpointJson]) = 1 AND [Lens] IN (N'Continuation', N'Breakout'))
);
GO
