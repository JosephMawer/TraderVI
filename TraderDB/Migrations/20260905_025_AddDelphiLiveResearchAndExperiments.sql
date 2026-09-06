:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
/* ADR-0053. Review individually after a verified backup and explicit permission.
   Adds empty immutable research/protocol audit tables. Creates no experiment,
   assignment, portfolio, market observation, training or operational activation.
   Requires the reviewed 022/023/024 source schema. Never deploy via DACPAC. */
IF OBJECT_ID(N'dbo.DelphiLivePortfolioLedger', N'U') IS NULL OR
   OBJECT_ID(N'dbo.DelphiLiveSession', N'U') IS NULL OR
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
COMMIT TRANSACTION;
GO
