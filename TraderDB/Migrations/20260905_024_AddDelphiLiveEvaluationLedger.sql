-- Manual source-only migration. Requires explicit authorization and a fresh verified backup.
-- Run in SQLCMD mode from the repository root; never deploy a DACPAC.
-- Requires the complete migration 022 foundation and an absent evaluation table.
-- Creates one empty evaluation table; no source evidence, evaluations, or actions are inserted.
-- DDL and precommit verification are transactional. After-commit rollback requires
-- restoring the verified pre-migration backup with separate authorization.
:ON ERROR EXIT
USE [TraderDB];
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.DelphiLiveSessionSymbol',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLivePolicyVersion',N'U') IS NULL
 THROW 51240,'Apply and verify the reviewed Delphi Live foundation first.',1;
IF OBJECT_ID(N'dbo.DelphiLiveEvaluation',N'U') IS NOT NULL
 THROW 51241,'Evaluation ledger already exists; review the prior installation.',1;
GO
:r TraderDB\dbo\Tables\DelphiLiveEvaluation.sql

IF OBJECT_ID(N'dbo.DelphiLiveEvaluation', N'U') IS NULL
    THROW 52303, 'The Delphi Live evaluation table was not created.', 1;
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveEvaluation)
    THROW 52304, 'Evaluation installation must not create checkpoint evidence.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DelphiLiveEvaluation')
           AND (is_disabled=1 OR is_not_trusted=1))
    OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.DelphiLiveEvaluation')
               AND (is_disabled=1 OR is_not_trusted=1))
    OR EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DelphiLiveEvaluation') AND is_disabled=1)
    THROW 52305, 'A new evaluation constraint or index is disabled or untrusted.', 1;
COMMIT TRANSACTION;
GO
