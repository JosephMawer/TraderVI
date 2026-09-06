CREATE TABLE [dbo].[DelphiLiveResearchOutcomeRevision]
(
    [RevisionId] UNIQUEIDENTIFIER NOT NULL,
    [SlotId] UNIQUEIDENTIFIER NOT NULL,
    [CalculatedUtc] DATETIME2 NOT NULL,
    [OutcomeJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveResearchOutcomeRevision] PRIMARY KEY ([RevisionId]),
    CONSTRAINT [UQ_DelphiLiveResearchOutcomeRevision_SlotTime] UNIQUE ([SlotId], [CalculatedUtc]),
    CONSTRAINT [FK_DelphiLiveResearchOutcomeRevision_Slot] FOREIGN KEY ([SlotId]) REFERENCES [dbo].[DelphiLiveExpectedResearchSlot] ([SlotId]),
    CONSTRAINT [CK_DelphiLiveResearchOutcomeRevision_Json] CHECK (ISJSON([OutcomeJson]) = 1)
);
GO
