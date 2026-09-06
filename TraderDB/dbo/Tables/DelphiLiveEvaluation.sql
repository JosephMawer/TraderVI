CREATE TABLE [dbo].[DelphiLiveEvaluation]
(
    [EvaluationId] UNIQUEIDENTIFIER NOT NULL,
    [SessionId] UNIQUEIDENTIFIER NOT NULL,
    [PolicyVersionId] UNIQUEIDENTIFIER NOT NULL,
    [Symbol] NVARCHAR(20) NOT NULL,
    [BarEndUtc] DATETIME2 NOT NULL,
    [ContinuityEpoch] INT NOT NULL,
    [LeaseFencingToken] BIGINT NOT NULL,
    [ObservedOnTime] BIT NOT NULL,
    [ConfirmedLiveEligible] BIT NOT NULL,
    [InputJson] NVARCHAR(MAX) NOT NULL,
    [ResultJson] NVARCHAR(MAX) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_DelphiLiveEvaluation_Recorded] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_DelphiLiveEvaluation] PRIMARY KEY ([EvaluationId]),
    CONSTRAINT [FK_DelphiLiveEvaluation_SessionSymbol] FOREIGN KEY ([SessionId],[Symbol]) REFERENCES [dbo].[DelphiLiveSessionSymbol] ([SessionId],[Symbol]),
    CONSTRAINT [FK_DelphiLiveEvaluation_Policy] FOREIGN KEY ([PolicyVersionId]) REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId]),
    CONSTRAINT [UQ_DelphiLiveEvaluation_Checkpoint] UNIQUE ([SessionId],[PolicyVersionId],[Symbol],[BarEndUtc]),
    CONSTRAINT [CK_DelphiLiveEvaluation_Json] CHECK (ISJSON([InputJson])=1 AND ISJSON([ResultJson])=1),
    CONSTRAINT [CK_DelphiLiveEvaluation_Causality] CHECK ([RecordedUtc]>[BarEndUtc] AND [ContinuityEpoch]>0 AND [LeaseFencingToken]>0),
    CONSTRAINT [CK_DelphiLiveEvaluation_Confirmation] CHECK ([ConfirmedLiveEligible]=0 OR [ObservedOnTime]=1)
);
GO
