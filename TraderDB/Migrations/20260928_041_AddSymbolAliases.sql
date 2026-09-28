-- ADR-0088: resolve source symbols at the monitoring boundary, retaining account history.
-- Preconditions: existing Symbols and reviewed broker imports; no existing SymbolAliases table.
-- Data effect: add one alias table and one explicitly reviewed Canadian listing alias.
-- No position, trade, import, risk, assignment or historical observation is updated.
-- Review this exact script and verify a fresh full backup before authorized manual execution.
-- Recovery: transaction failure rolls back this addition. After commit, retain the additive table;
-- stop the updated monitor and review any correction separately. Do not rewrite account history.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Symbols',N'U') IS NULL OR OBJECT_ID(N'dbo.BrokerAccountImport',N'U') IS NULL
    THROW 51700,'Symbols and reviewed broker imports are required.',1;
IF OBJECT_ID(N'dbo.SymbolAliases') IS NOT NULL
    THROW 51701,'SymbolAliases already exists. Inspect before proceeding.',1;
IF NOT EXISTS (SELECT 1 FROM dbo.Symbols WHERE Symbol=N'EDR' AND SecurityType=N'Stock'
    AND (ExchangeCode IS NULL OR ExchangeCode IN(N'TSX',N'XTSE')))
    OR EXISTS (SELECT 1 FROM dbo.Symbols WHERE Symbol=N'EDR.TO')
    THROW 51702,'The reviewed canonical listing is missing or conflicting.',1;
-- BrokerTradeProjection uses .TO only for verified CAD/XTSE listings. The canonical
-- EDR row currently has no exchange code; this explicit mapping does not change that row.
IF NOT EXISTS (
    SELECT 1 FROM dbo.BrokerAccountImport b
    CROSS APPLY OPENJSON(b.PlanJson,'$.Observation.Instruments') WITH
        (Symbol nvarchar(20),PrimaryMic nvarchar(20),Currency nvarchar(10),SecurityType nvarchar(20)) i
    WHERE i.Symbol=N'EDR' AND i.PrimaryMic=N'XTSE' AND i.Currency=N'CAD' AND i.SecurityType=N'EQUITY')
    THROW 51703,'Saved broker listing evidence does not support the reviewed alias.',1;
GO
CREATE TABLE [dbo].[SymbolAliases]
(
    [Source] NVARCHAR(32) NOT NULL,
    [Alias] NVARCHAR(20) NOT NULL,
    [Symbol] NVARCHAR(20) NOT NULL,
    [ExchangeMic] NVARCHAR(4) NOT NULL,
    [VerifiedUtc] DATETIME2(7) NOT NULL,
    CONSTRAINT [PK_SymbolAliases] PRIMARY KEY ([Source], [Alias]),
    CONSTRAINT [FK_SymbolAliases_Symbol] FOREIGN KEY ([Symbol]) REFERENCES [dbo].[Symbols]([Symbol]),
    CONSTRAINT [CK_SymbolAliases_Content] CHECK
        (LEN(LTRIM(RTRIM([Source]))) > 0 AND LEN(LTRIM(RTRIM([Alias]))) > 0
         AND [Alias] <> [Symbol] AND LEN([ExchangeMic]) = 4)
);
GO
INSERT dbo.SymbolAliases(Source,Alias,Symbol,ExchangeMic,VerifiedUtc)
VALUES(N'Broker',N'EDR.TO',N'EDR',N'XTSE',SYSUTCDATETIME());
IF (SELECT COUNT(*) FROM dbo.SymbolAliases) <> 1 OR NOT EXISTS (
    SELECT 1 FROM dbo.SymbolAliases WHERE Source=N'Broker' AND Alias=N'EDR.TO'
        AND Symbol=N'EDR' AND ExchangeMic=N'XTSE')
    THROW 51704,'Symbol alias postconditions failed.',1;
COMMIT TRANSACTION;
GO
