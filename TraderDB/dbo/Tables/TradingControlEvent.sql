CREATE TABLE [dbo].[TradingControlEvent]
(
    [EventId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_TradingControlEvent] PRIMARY KEY,
    [SystemKey] NVARCHAR(32) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL,
    [RecordedBy] NVARCHAR(128) NOT NULL,
    [Paused] BIT NOT NULL,
    [Reason] NVARCHAR(512) NOT NULL,
    [PriorStateJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [CK_TradingControlEvent_Content] CHECK (ISJSON([PriorStateJson])=1 AND LEN(LTRIM(RTRIM([Reason])))>0)
);
