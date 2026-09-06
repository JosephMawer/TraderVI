CREATE TABLE [dbo].[DelphiLiveExperimentRevision]
(
    [ProtocolId] UNIQUEIDENTIFIER NOT NULL,
    [Revision] BIGINT NOT NULL,
    [SnapshotJson] NVARCHAR(MAX) NOT NULL,
    [PersistedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_DelphiLiveExperimentRevision_Persisted] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_DelphiLiveExperimentRevision] PRIMARY KEY ([ProtocolId], [Revision]),
    CONSTRAINT [FK_DelphiLiveExperimentRevision_Protocol] FOREIGN KEY ([ProtocolId]) REFERENCES [dbo].[DelphiLiveExperimentProtocol] ([ProtocolId]),
    CONSTRAINT [CK_DelphiLiveExperimentRevision_Json] CHECK ([Revision] >= 0 AND ISJSON([SnapshotJson]) = 1)
);
GO
