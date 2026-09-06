CREATE TABLE [dbo].[DelphiLiveResearchSessionReview]
(
    [SessionId] UNIQUEIDENTIFIER NOT NULL,
    [ReviewedUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_DelphiLiveResearchSessionReview] PRIMARY KEY ([SessionId],[ReviewedUtc]),
    CONSTRAINT [FK_DelphiLiveResearchSessionReview_Session] FOREIGN KEY ([SessionId]) REFERENCES [dbo].[DelphiLiveSession] ([SessionId])
);
GO
