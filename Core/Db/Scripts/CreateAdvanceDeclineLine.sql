-- Granville's Advance-Decline Line (daily market breadth)
-- Tracks the cumulative net of advancing vs declining TSX issues.
-- The running CumulativeDifferential IS the A/D Line value.

CREATE TABLE [dbo].[AdvanceDeclineLine]
(
    [Date]                   DATE          NOT NULL,
    [Advancers]              INT           NOT NULL,
    [Decliners]              INT           NOT NULL,
    [Unchanged]              INT           NOT NULL,
    [DailyPlurality]         INT           NOT NULL,   -- Advancers - Decliners
    [CumulativeDifferential] INT           NOT NULL,   -- Running sum (the A/D Line)
    [XiuClose]               REAL          NULL,       -- XIU benchmark close for divergence analysis

    CONSTRAINT [PK_AdvanceDeclineLine] PRIMARY KEY CLUSTERED ([Date] ASC)
);

-- Fast lookups for recent range queries
CREATE NONCLUSTERED INDEX [IX_ADLine_Date]
ON [dbo].[AdvanceDeclineLine] ([Date] DESC)
INCLUDE ([CumulativeDifferential], [XiuClose]);