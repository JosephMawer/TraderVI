-- Manual source-only migration. Requires explicit authorization and a fresh verified backup.
-- Run in SQLCMD mode from the repository root; never deploy a DACPAC.
:ON ERROR EXIT
USE [TraderDB];
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.DelphiLiveSessionSymbol',N'U') IS NULL OR OBJECT_ID(N'dbo.DelphiLivePolicyVersion',N'U') IS NULL
 THROW 51240,'Apply and verify the reviewed Delphi Live foundation first.',1;
IF OBJECT_ID(N'dbo.DelphiLiveEvaluation',N'U') IS NOT NULL
 THROW 51241,'Evaluation ledger already exists; review the prior installation.',1;
GO
:r TraderDB\dbo\Tables\DelphiLiveEvaluation.sql
COMMIT TRANSACTION;
GO
