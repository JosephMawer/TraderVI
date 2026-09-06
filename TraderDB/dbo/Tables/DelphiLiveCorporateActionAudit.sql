CREATE TABLE [dbo].[DelphiLiveCorporateActionAudit]
(
    [AuditId] UNIQUEIDENTIFIER NOT NULL,
    [Symbol] NVARCHAR(20) NOT NULL,
    [AffectedFrom] DATE NOT NULL,
    [AffectedThrough] DATE NOT NULL,
    [RecordedUtc] DATETIME2 NOT NULL,
    [AuditJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveCorporateActionAudit] PRIMARY KEY ([AuditId]),
    CONSTRAINT [FK_DelphiLiveCorporateActionAudit_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols] ([Symbol]),
    CONSTRAINT [CK_DelphiLiveCorporateActionAudit_Content] CHECK ([AffectedThrough]>=[AffectedFrom] AND ISJSON([AuditJson])=1)
);
GO
CREATE INDEX [IX_DelphiLiveCorporateActionAudit_SymbolDate] ON [dbo].[DelphiLiveCorporateActionAudit] ([Symbol],[AffectedFrom],[AffectedThrough]);
GO
