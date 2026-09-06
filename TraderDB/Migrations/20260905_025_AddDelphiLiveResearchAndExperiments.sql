:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
/* ADR-0053. Review individually after a verified backup and explicit permission.
   Adds eight empty immutable research/protocol audit tables. Creates no experiment,
   assignment, portfolio, market observation, training or operational activation.
   Requires the reviewed 022/023/024 source schema. Never deploy via DACPAC.
   DDL and precommit verification form one transaction. After-commit rollback
   requires restoring the verified pre-migration backup with separate authorization. */
IF OBJECT_ID(N'dbo.DelphiLivePortfolioLedger', N'U') IS NULL OR
   OBJECT_ID(N'dbo.DelphiLiveSession', N'U') IS NULL OR
   OBJECT_ID(N'dbo.DelphiLiveEvaluation', N'U') IS NULL OR
   COL_LENGTH(N'dbo.DelphiLivePortfolioGeneration', N'EndExclusiveTradingDate') IS NULL
    THROW 51260, 'Review and apply the prerequisite Delphi Live foundation and ledger schemas first.', 1;
IF OBJECT_ID(N'dbo.DelphiLiveExperimentProtocol', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveExperimentRevision', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveExperimentEvent', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveExpectedResearchSlot', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveResearchOutcomeRevision', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveRankingCheckpoint', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveCorporateActionAudit', N'U') IS NOT NULL OR
   OBJECT_ID(N'dbo.DelphiLiveResearchSessionReview', N'U') IS NOT NULL
    THROW 51261, 'Existing or partial research/experiment installation requires individual review.', 1;
BEGIN TRANSACTION;
GO
:r TraderDB\dbo\Tables\DelphiLiveExperimentProtocol.sql
:r TraderDB\dbo\Tables\DelphiLiveExperimentRevision.sql
:r TraderDB\dbo\Tables\DelphiLiveExperimentEvent.sql
:r TraderDB\dbo\Tables\DelphiLiveExpectedResearchSlot.sql
:r TraderDB\dbo\Tables\DelphiLiveResearchOutcomeRevision.sql
:r TraderDB\dbo\Tables\DelphiLiveRankingCheckpoint.sql
:r TraderDB\dbo\Tables\DelphiLiveCorporateActionAudit.sql
:r TraderDB\dbo\Tables\DelphiLiveResearchSessionReview.sql

DECLARE @NewTables TABLE ([ObjectId] INT NULL);
INSERT @NewTables ([ObjectId]) VALUES
    (OBJECT_ID(N'dbo.DelphiLiveExperimentProtocol', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveExperimentRevision', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveExperimentEvent', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveExpectedResearchSlot', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveResearchOutcomeRevision', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveRankingCheckpoint', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveCorporateActionAudit', N'U')),
    (OBJECT_ID(N'dbo.DelphiLiveResearchSessionReview', N'U'));
IF (SELECT COUNT([ObjectId]) FROM @NewTables) <> 8
    THROW 52306, 'The complete eight-table Delphi Live research and experiment schema was not created.', 1;
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveExperimentProtocol)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveExperimentRevision)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveExperimentEvent)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveExpectedResearchSlot)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveResearchOutcomeRevision)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveRankingCheckpoint)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveCorporateActionAudit)
    OR EXISTS (SELECT 1 FROM dbo.DelphiLiveResearchSessionReview)
    THROW 52307, 'Research installation must not create experiments, checkpoints, outcomes, audits, or reviews.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys k JOIN @NewTables t ON t.ObjectId=k.parent_object_id
           WHERE k.is_disabled=1 OR k.is_not_trusted=1)
    OR EXISTS (SELECT 1 FROM sys.check_constraints k JOIN @NewTables t ON t.ObjectId=k.parent_object_id
               WHERE k.is_disabled=1 OR k.is_not_trusted=1)
    OR EXISTS (SELECT 1 FROM sys.indexes i JOIN @NewTables t ON t.ObjectId=i.object_id WHERE i.is_disabled=1)
    THROW 52308, 'A new research or experiment constraint or index is disabled or untrusted.', 1;
COMMIT TRANSACTION;
GO
