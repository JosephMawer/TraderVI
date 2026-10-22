CREATE TABLE [dbo].[EngineInputPublication]
(
    [PublicationSequence] BIGINT IDENTITY(1,1) NOT NULL,
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
    [RecordedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineInputPublication_Recorded] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_EngineInputPublication] PRIMARY KEY ([EvaluationId]),
    CONSTRAINT [UQ_EngineInputPublication_Sequence] UNIQUE ([PublicationSequence]),
    CONSTRAINT [FK_EngineInputPublication_SessionSymbol] FOREIGN KEY ([SessionId],[Symbol]) REFERENCES [dbo].[DelphiLiveSessionSymbol] ([SessionId],[Symbol]),
    CONSTRAINT [FK_EngineInputPublication_Policy] FOREIGN KEY ([PolicyVersionId]) REFERENCES [dbo].[DelphiLivePolicyVersion] ([DelphiLivePolicyVersionId]),
    CONSTRAINT [UQ_EngineInputPublication_Checkpoint] UNIQUE ([SessionId],[PolicyVersionId],[Symbol],[BarEndUtc]),
    CONSTRAINT [CK_EngineInputPublication_Json] CHECK (ISJSON([InputJson])=1 AND ISJSON([ResultJson])=1),
    CONSTRAINT [CK_EngineInputPublication_Version] CHECK (JSON_VALUE([InputJson],'$."$evidence".version') IS NOT NULL AND JSON_VALUE([InputJson],'$."$evidence".version')=N'1'),
    CONSTRAINT [CK_EngineInputPublication_Causality] CHECK ([RecordedUtc]>[BarEndUtc] AND [ContinuityEpoch]>0 AND [LeaseFencingToken]>0),
    CONSTRAINT [CK_EngineInputPublication_Confirmation] CHECK ([ConfirmedLiveEligible]=0 OR [ObservedOnTime]=1)
);
GO
