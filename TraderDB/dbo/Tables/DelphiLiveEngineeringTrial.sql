CREATE TABLE [dbo].[DelphiLiveEngineeringTrial]
(
    [TrialId] UNIQUEIDENTIFIER NOT NULL,
    [TradingDate] DATE NOT NULL,
    [CalibrationRunId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorizedUtc] DATETIME2 NOT NULL,
    [AuthorizedBy] NVARCHAR(128) NOT NULL,
    [Reason] NVARCHAR(1024) NOT NULL,
    [EvidenceJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveEngineeringTrial] PRIMARY KEY ([TrialId]),
    CONSTRAINT [UQ_DelphiLiveEngineeringTrial_Date] UNIQUE ([TrialId], [TradingDate]),
    CONSTRAINT [UQ_DelphiLiveEngineeringTrial_Run] UNIQUE ([TrialId], [CalibrationRunId]),
    CONSTRAINT [FK_DelphiLiveEngineeringTrial_Run] FOREIGN KEY ([CalibrationRunId]) REFERENCES [dbo].[CalibrationRun] ([RunId]),
    CONSTRAINT [CK_DelphiLiveEngineeringTrial_OneSession] CHECK
    (
        [TrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908'
        AND [TradingDate] = '20260908'
        AND [CalibrationRunId] = '9491e421-ef3e-48b4-bcb7-389c42ec25e9'
        AND [AuthorizedUtc] >= '2026-09-08T17:50:00'
        AND [AuthorizedUtc] < '2026-09-08T19:15:00'
        AND LEN(LTRIM(RTRIM([AuthorizedBy]))) > 0
        AND LEN(LTRIM(RTRIM([Reason]))) > 0
        AND ISJSON([EvidenceJson]) = 1
    )
);
GO
