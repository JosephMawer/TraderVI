:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

/*
    ADR-0053: additive inactive Delphi Live portfolio persistence.
    Review and run from the repository root with SQLCMD only after a fresh
    verified backup and explicit authorization. Do not deploy a DACPAC.
    Requires migration 022. Creates four empty tables and no assignments,
    capital, portfolios, actions, fills, or operational activation.
    Snapshot revisions and ledger events are append-only; the current snapshot
    is updated only in an expected-revision, durable-lease-fenced transaction.
    Roll back after commit by restoring the verified pre-migration backup;
    never rewrite completed historical portfolio evidence.
*/
IF OBJECT_ID(N'dbo.DelphiLivePolicyAssignment', N'U') IS NULL OR
   OBJECT_ID(N'dbo.DelphiLiveHostLease', N'U') IS NULL OR
   OBJECT_ID(N'dbo.DelphiLivePolicyVersion', N'U') IS NULL
    THROW 51230, 'Migration 022 must be reviewed and applied before portfolio persistence.', 1;
IF OBJECT_ID(N'dbo.DelphiLivePortfolioGeneration', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLivePortfolioLedger', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLivePortfolioRevision', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveLedgerEvent', N'U') IS NOT NULL
    THROW 51231, 'Review the partial or prior Delphi Live portfolio installation; no existing ledger is overwritten.', 1;

BEGIN TRANSACTION;
GO
:r TraderDB\dbo\Tables\DelphiLivePortfolioGeneration.sql
:r TraderDB\dbo\Tables\DelphiLivePortfolioLedger.sql
:r TraderDB\dbo\Tables\DelphiLivePortfolioRevision.sql
:r TraderDB\dbo\Tables\DelphiLiveLedgerEvent.sql

DECLARE @NewTables TABLE ([ObjectId] INT NULL);
INSERT @NewTables ([ObjectId]) VALUES
    (OBJECT_ID(N'dbo.DelphiLivePortfolioGeneration', N'U')),
    (OBJECT_ID(N'dbo.DelphiLivePortfolioLedger', N'U')),
    (OBJECT_ID(N'dbo.DelphiLivePortfolioRevision', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveLedgerEvent', N'U'));
IF (SELECT COUNT([ObjectId]) FROM @NewTables) <> 4
    THROW 52300, 'The complete four-table Delphi Live portfolio schema was not created.', 1;
IF EXISTS (SELECT 1 FROM dbo.DelphiLivePortfolioGeneration)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLivePortfolioLedger)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLivePortfolioRevision)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveLedgerEvent)
    THROW 52301, 'Portfolio installation must not create capital, portfolios, revisions, or events.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys k JOIN @NewTables t ON t.ObjectId=k.parent_object_id
           WHERE k.is_disabled=1 OR k.is_not_trusted=1)
    OR EXISTS (SELECT 1 FROM sys.check_constraints k JOIN @NewTables t ON t.ObjectId=k.parent_object_id
               WHERE k.is_disabled=1 OR k.is_not_trusted=1)
    OR EXISTS (SELECT 1 FROM sys.indexes i JOIN @NewTables t ON t.ObjectId=i.object_id WHERE i.is_disabled=1)
    THROW 52302, 'A new portfolio constraint or index is disabled or untrusted.', 1;
COMMIT TRANSACTION;
GO
