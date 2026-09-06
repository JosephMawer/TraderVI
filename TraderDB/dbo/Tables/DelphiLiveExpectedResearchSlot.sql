CREATE TABLE [dbo].[DelphiLiveExpectedResearchSlot]
(
    [SlotId] UNIQUEIDENTIFIER NOT NULL,
    [SessionId] UNIQUEIDENTIFIER NOT NULL,
    [TradingDate] DATE NOT NULL,
    [BarEndUtc] DATETIME2 NOT NULL,
    [Symbol] NVARCHAR(20) NOT NULL,
    [IsBenchmark] BIT NOT NULL,
    [SlotJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveExpectedResearchSlot] PRIMARY KEY ([SlotId]),
    CONSTRAINT [UQ_DelphiLiveExpectedResearchSlot_Expected] UNIQUE ([SessionId], [Symbol], [BarEndUtc]),
    CONSTRAINT [FK_DelphiLiveExpectedResearchSlot_Session] FOREIGN KEY ([SessionId]) REFERENCES [dbo].[DelphiLiveSession] ([SessionId]),
    CONSTRAINT [CK_DelphiLiveExpectedResearchSlot_Json] CHECK (ISJSON([SlotJson]) = 1 AND
        (([Symbol] = N'XIU' AND [IsBenchmark] = 1) OR ([Symbol] <> N'XIU' AND [IsBenchmark] = 0)))
);
GO
