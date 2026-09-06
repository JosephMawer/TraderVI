CREATE TABLE [dbo].[DelphiLiveExperimentEvent]
(
    [CommandId] UNIQUEIDENTIFIER NOT NULL,
    [ProtocolId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [EventKind] NVARCHAR(64) NOT NULL,
    [DataJson] NVARCHAR(MAX) NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL,
    CONSTRAINT [PK_DelphiLiveExperimentEvent] PRIMARY KEY ([CommandId]),
    CONSTRAINT [FK_DelphiLiveExperimentEvent_Revision] FOREIGN KEY ([ProtocolId], [Revision]) REFERENCES [dbo].[DelphiLiveExperimentRevision] ([ProtocolId], [Revision]),
    CONSTRAINT [CK_DelphiLiveExperimentEvent_Json] CHECK (ISJSON([DataJson]) = 1 AND LEN(LTRIM(RTRIM([EventKind]))) > 0)
);
GO
