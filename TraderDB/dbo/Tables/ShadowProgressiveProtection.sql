CREATE TABLE [dbo].[ShadowProgressiveProtection]
(
    [PositionId] UNIQUEIDENTIFIER NOT NULL,
    [StateJson] NVARCHAR(MAX) NOT NULL,
    [UpdatedUtc] DATETIME2 NOT NULL CONSTRAINT [DF_ShadowProgressiveProtection_UpdatedUtc] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ShadowProgressiveProtection] PRIMARY KEY ([PositionId]),
    CONSTRAINT [FK_ShadowProgressiveProtection_Position] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[ShadowPosition]([PositionId]),
    CONSTRAINT [CK_ShadowProgressiveProtection_Json] CHECK (ISJSON([StateJson])=1)
);
