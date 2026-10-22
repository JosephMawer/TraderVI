CREATE TABLE [dbo].[EngineIntegrityResolution]
(
    [IncidentId] UNIQUEIDENTIFIER NOT NULL,
    [RecoveryRunId] UNIQUEIDENTIFIER NOT NULL,
    [VerifiedHeadsHash] CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ClearedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_EngineIntegrityResolution_Time] DEFAULT SYSUTCDATETIME(),
    [OperatorName] NVARCHAR(128) NOT NULL,
    [Reason] NVARCHAR(500) NOT NULL,
    CONSTRAINT [PK_EngineIntegrityResolution] PRIMARY KEY ([IncidentId]),
    CONSTRAINT [FK_EngineIntegrityResolution_Incident] FOREIGN KEY ([IncidentId]) REFERENCES [dbo].[EngineIntegrityIncident]([IncidentId]),
    CONSTRAINT [FK_EngineIntegrityResolution_Recovery] FOREIGN KEY ([RecoveryRunId]) REFERENCES [dbo].[EngineAuditRun]([RunId]),
    CONSTRAINT [CK_EngineIntegrityResolution_Proof] CHECK (LEN([VerifiedHeadsHash])=64 AND [VerifiedHeadsHash] NOT LIKE '%[^0-9A-F]%'
        AND LEN(LTRIM(RTRIM([OperatorName])))>0 AND LEN(LTRIM(RTRIM([Reason])))>0)
);
GO
