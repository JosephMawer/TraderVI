CREATE TABLE [dbo].[CalibrationOutcomeDefinition]
(
    [OutcomeDefinitionId] UNIQUEIDENTIFIER NOT NULL,
    [DefinitionName]      NVARCHAR(64)     NOT NULL,
    [DefinitionVersion]   INT              NOT NULL,
    [DefinitionKind]      NVARCHAR(24)     NOT NULL,
    [DefinitionJson]      NVARCHAR(MAX)    NOT NULL,
    [IsActive]            BIT              NOT NULL,
    [CreatedUtc]          DATETIME2        NOT NULL CONSTRAINT [DF_CalibrationOutcomeDefinition_CreatedUtc] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_CalibrationOutcomeDefinition] PRIMARY KEY CLUSTERED ([OutcomeDefinitionId]),
    CONSTRAINT [UQ_CalibrationOutcomeDefinition_NameVersion] UNIQUE ([DefinitionName], [DefinitionVersion]),
    CONSTRAINT [CK_CalibrationOutcomeDefinition_Kind] CHECK ([DefinitionKind] IN ('Prediction', 'Tradeable', 'Portfolio'))
);
