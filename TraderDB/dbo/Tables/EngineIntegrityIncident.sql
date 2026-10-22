CREATE TABLE [dbo].[EngineIntegrityIncident]
(
    [IncidentId] UNIQUEIDENTIFIER NOT NULL,
    [Scope] NVARCHAR(16) NOT NULL,
    [OwnerId] UNIQUEIDENTIFIER NULL,
    [EvidenceKey] NVARCHAR(200) NOT NULL,
    [FailureCode] NVARCHAR(64) NOT NULL,
    [DiscoveredUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineIntegrityIncident_Time] DEFAULT SYSUTCDATETIME(),
    [ProtectionUntrusted] BIT NOT NULL,
    CONSTRAINT [PK_EngineIntegrityIncident] PRIMARY KEY ([IncidentId]),
    CONSTRAINT [CK_EngineIntegrityIncident_Scope] CHECK ([Scope] IN(N'Global',N'Broker',N'Paper',N'Research')
        AND ([Scope]<>N'Global' OR [OwnerId] IS NULL))
);
GO
