/* ADR-0065: explicitly authorized, single-date Delphi Live engineering admission.
Run only through the reviewed Admission helper with a caller-owned serializable
transaction and @PreparedJson parameter. Rehearsal rolls back schema and data.
No time, price, eligibility or gate evidence is rewritten. Normal runtime source
selection and activation APIs remain unchanged. A backup is required before apply.
Recovery after commit: pause Live buys through the normal control; preserve paper
holdings, protective exits and evidence. Never restore over subsequent activity. */
SET NOCOUNT ON; SET XACT_ABORT ON; SET LOCK_TIMEOUT 15000;
IF DB_NAME()<>N'TraderDB' OR @@TRANCOUNT<>1 THROW 51431,'Expected fenced TraderDB transaction.',1;
IF SYSUTCDATETIME()<'2026-09-08T17:50:00' OR SYSUTCDATETIME()>='2026-09-08T19:15:00'
 THROW 51431,'Outside this authorized one-session activation window.',1;
IF OBJECT_ID(N'dbo.DelphiLiveEngineeringTrial',N'U') IS NOT NULL
 OR EXISTS(SELECT 1 FROM dbo.DelphiLivePortfolioLedger)
 OR EXISTS(SELECT 1 FROM dbo.DelphiLivePolicyAssignment)
 OR EXISTS(SELECT 1 FROM dbo.DelphiLiveSession)
 OR EXISTS(SELECT 1 FROM dbo.DelphiLiveHostLease WHERE IsHeld=1 AND ExpiresUtc>SYSUTCDATETIME())
 THROW 51431,'Live state exists; no overwrite or repeated activation is allowed.',1;
IF EXISTS(SELECT 1 FROM dbo.TradingSystemControl WHERE SystemKey IN(N'Daily',N'DelphiLive') AND Paused=1)
 THROW 51431,'An entry pause is active; no pause is cleared by this operation.',1;
IF EXISTS(SELECT 1 FROM dbo.EngineStrategyAssignment a JOIN dbo.EngineStrategyVersion v ON v.VersionId=a.VersionId WHERE v.Family=N'DelphiLive')
 THROW 51431,'Live strategy overrides need separate review.',1;
GO
CREATE TABLE [dbo].[DelphiLiveEngineeringTrial]
(
    [TrialId] UNIQUEIDENTIFIER NOT NULL,
    [TradingDate] DATE NOT NULL,
    [CalibrationRunId] UNIQUEIDENTIFIER NOT NULL,
    [AuthorizedUtc] DATETIME2 NOT NULL,
    [AuthorizedBy] NVARCHAR(128) NOT NULL,
    [Reason] NVARCHAR(1024) NOT NULL,
    [EvidenceJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_DelphiLiveEngineeringTrial] PRIMARY KEY ([TrialId]),
    CONSTRAINT [UQ_DelphiLiveEngineeringTrial_Date] UNIQUE ([TrialId], [TradingDate]),
    CONSTRAINT [UQ_DelphiLiveEngineeringTrial_Run] UNIQUE ([TrialId], [CalibrationRunId]),
    CONSTRAINT [FK_DelphiLiveEngineeringTrial_Run] FOREIGN KEY ([CalibrationRunId]) REFERENCES [dbo].[CalibrationRun] ([RunId]),
    CONSTRAINT [CK_DelphiLiveEngineeringTrial_OneSession] CHECK
    (
        [TrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908'
        AND [TradingDate] = '20260908'
        AND [CalibrationRunId] = '9491e421-ef3e-48b4-bcb7-389c42ec25e9'
        AND [AuthorizedUtc] >= '2026-09-08T17:50:00'
        AND [AuthorizedUtc] < '2026-09-08T19:15:00'
        AND LEN(LTRIM(RTRIM([AuthorizedBy]))) > 0
        AND LEN(LTRIM(RTRIM([Reason]))) > 0
        AND ISJSON([EvidenceJson]) = 1
    )
);
GO


ALTER TABLE dbo.[DelphiLivePortfolioGeneration] ADD [EngineeringTrialId] UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE dbo.[DelphiLivePortfolioGeneration] DROP CONSTRAINT [CK_DelphiLivePortfolioGeneration_Authorization];
ALTER TABLE dbo.[DelphiLivePortfolioGeneration] WITH CHECK ADD CONSTRAINT [CK_DelphiLivePortfolioGeneration_Authorization] CHECK
        ((([EngineeringTrialId] IS NULL AND [AuthorizedUtc] < [EffectiveSessionOpenUtc]) OR
          ([EngineeringTrialId] IS NOT NULL AND [EngineeringTrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908' AND [EffectiveTradingDate] = '20260908'
           AND [EffectiveSessionOpenUtc] = '2026-09-08T13:30:00' AND [AuthorizedUtc] >= '2026-09-08T17:50:00'
           AND [AuthorizedUtc] < '2026-09-08T19:15:00' AND [StartingCapital] = 1000 AND [Currency] = 'CAD'))
         AND LEN(LTRIM(RTRIM([AuthorizedBy]))) > 0 AND LEN(LTRIM(RTRIM([AuthorizationReason]))) > 0 AND ISJSON([AuthorizationJson]) = 1);
ALTER TABLE dbo.[DelphiLivePortfolioGeneration] WITH CHECK ADD CONSTRAINT [FK_DelphiLivePortfolioGeneration_Trial] FOREIGN KEY ([EngineeringTrialId], [EffectiveTradingDate]) REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId], [TradingDate]);
GO

ALTER TABLE dbo.[DelphiLiveSession] ADD [EngineeringTrialId] UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE dbo.[DelphiLiveSession] DROP CONSTRAINT [CK_DelphiLiveSession_FreezeStatus];
ALTER TABLE dbo.[DelphiLiveSession] DROP CONSTRAINT [CK_DelphiLiveSession_FrozenRun];
ALTER TABLE dbo.[DelphiLiveSession] WITH CHECK ADD CONSTRAINT [CK_DelphiLiveSession_FreezeStatus] CHECK ([FreezeStatus] IN (N'FrozenOfficialRun', N'NoValidDelphiRun', N'OperatorEngineeringTest'));
ALTER TABLE dbo.[DelphiLiveSession] WITH CHECK ADD CONSTRAINT [CK_DelphiLiveSession_FrozenRun] CHECK
    (
        (
            [FreezeStatus] = N'FrozenOfficialRun'
            AND [EngineeringTrialId] IS NULL
            AND [CalibrationRunId] IS NOT NULL
            AND [DailyStrategyVersionId] IS NOT NULL
            AND [CalibrationRunPurpose] = N'OfficialPaper'
            AND [CalibrationRunAuditState] = N'Valid'
            AND [CalibrationRecommendationDate] = [TradingDate]
            AND [CalibrationMarketDataAsOf] = [ExpectedPriorCanonicalSessionDate]
            AND [CalibrationRunStartedUtc] IS NOT NULL
            AND [CalibrationRunCreatedUtc] IS NOT NULL
            AND [CalibrationRunCreatedUtc] <= [FreezeBoundaryUtc]
        )
        OR
        (
            [FreezeStatus] = N'NoValidDelphiRun'
            AND [EngineeringTrialId] IS NULL
            AND [CalibrationRunId] IS NULL
            AND [DailyStrategyVersionId] IS NULL
            AND [CalibrationRunPurpose] IS NULL
            AND [CalibrationRunAuditState] IS NULL
            AND [CalibrationRecommendationDate] IS NULL
            AND [CalibrationMarketDataAsOf] IS NULL
            AND [CalibrationRunStartedUtc] IS NULL
            AND [CalibrationRunCreatedUtc] IS NULL
        )
        OR
        (
            [FreezeStatus] = N'OperatorEngineeringTest'
            AND [EngineeringTrialId] IS NOT NULL
            AND [EngineeringTrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908'
            AND [TradingDate] = '20260908' AND [HostGapObserved] = 1
            AND [CalibrationRunId] = '9491e421-ef3e-48b4-bcb7-389c42ec25e9'
            AND [DailyStrategyVersionId] IS NOT NULL
            AND [CalibrationRunPurpose] = N'OfficialPaper' AND [CalibrationRunAuditState] = N'Valid'
            AND [CalibrationRecommendationDate] = [TradingDate]
            AND [CalibrationMarketDataAsOf] = [ExpectedPriorCanonicalSessionDate]
            AND [CalibrationRunStartedUtc] IS NOT NULL AND [CalibrationRunCreatedUtc] IS NOT NULL
            AND [CalibrationRunCreatedUtc] > [FreezeBoundaryUtc] AND [CalibrationRunCreatedUtc] <= [FrozenUtc]
        )
    );
ALTER TABLE dbo.[DelphiLiveSession] WITH CHECK ADD CONSTRAINT [FK_DelphiLiveSession_TrialDate] FOREIGN KEY ([EngineeringTrialId], [TradingDate])
        REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId], [TradingDate]);
ALTER TABLE dbo.[DelphiLiveSession] WITH CHECK ADD CONSTRAINT [FK_DelphiLiveSession_TrialRun] FOREIGN KEY ([EngineeringTrialId], [CalibrationRunId])
        REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId], [CalibrationRunId]);
GO

ALTER TABLE dbo.[DelphiLiveFrozenCandidateLens] ADD [EngineeringTrialId] UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE dbo.[DelphiLiveFrozenCandidateLens] DROP CONSTRAINT [CK_DelphiLiveFrozenCandidateLens_Identity];
ALTER TABLE dbo.[DelphiLiveFrozenCandidateLens] WITH CHECK ADD CONSTRAINT [CK_DelphiLiveFrozenCandidateLens_Identity] CHECK
    (
        [Lens] IN (N'Continuation', N'Breakout')
        AND (([EngineeringTrialId] IS NULL AND [IsEligible] = 1)
          OR ([EngineeringTrialId] IS NOT NULL AND [EngineeringTrialId] = 'c6d4639d-9c06-4c61-bde3-d76a6fdf1908' AND [IsEligible] = 0
            AND [Direction] = N'Hold' AND [FirstFailedGate] = N'Breadth'))
        AND [IsPublished] = 1
        AND [FrozenRank] BETWEEN 1 AND 25
    );
ALTER TABLE dbo.[DelphiLiveFrozenCandidateLens] WITH CHECK ADD CONSTRAINT [FK_DelphiLiveFrozenCandidateLens_Trial] FOREIGN KEY ([EngineeringTrialId])
        REFERENCES [dbo].[DelphiLiveEngineeringTrial] ([TrialId]);
GO

DECLARE @Now datetime2=SYSUTCDATETIME(), @Date date='20260908', @Open datetime2='2026-09-08T13:30:00',
 @Close datetime2='2026-09-08T20:00:00', @Run uniqueidentifier='9491e421-ef3e-48b4-bcb7-389c42ec25e9',
 @Strategy uniqueidentifier='48864449-92f4-4e33-99cf-40039210786e',
 @Trial uniqueidentifier='c6d4639d-9c06-4c61-bde3-d76a6fdf1908',
 @Session uniqueidentifier='dec4c9e0-f069-48bc-903b-c344fc5f1908',
 @Assignment uniqueidentifier='ff2a4c8b-aa0d-4860-a727-ceda435f1908',
 @Generation uniqueidentifier='8b8f1320-1e13-4168-aa48-32e0ae4f1908',
 @Portfolio uniqueidentifier='ae6e0f58-a5f0-44b4-8c73-ea2563ff1908',
 @Policy uniqueidentifier=TRY_CONVERT(uniqueidentifier,JSON_VALUE(@PreparedJson,'$.PolicyId')),
 @Reason nvarchar(1024)=N'Operator authorized Delphi Live today with CAD 1000 simulated capital. September 8 engineering test: admit the late recovered report and its published Hold/Breadth candidates; retain source facts, fresh confirmation, risk controls, causal fills and incomplete-session exclusion. ADR-0065.';
IF ISJSON(@PreparedJson)<>1 OR TRY_CONVERT(datetime2,JSON_VALUE(@PreparedJson,'$.PreparedUtc'))<DATEADD(minute,-45,@Now)
 OR NOT EXISTS(SELECT 1 FROM dbo.DelphiLivePolicyVersion WHERE DelphiLivePolicyVersionId=@Policy)
 THROW 51431,'Prepared baseline or installed policy is unavailable/stale.',1;
IF (SELECT COUNT(*) FROM dbo.StrategyVersion WHERE IsActive=1)<>1
 OR NOT EXISTS(SELECT 1 FROM dbo.StrategyVersion WHERE VersionId=@Strategy AND IsActive=1)
 OR NOT EXISTS(SELECT 1 FROM dbo.CalibrationRun WHERE RunId=@Run AND StrategyVersionId=@Strategy AND AuditState=N'Valid'
  AND RunPurpose=N'OfficialPaper' AND RecommendationDate=@Date AND MarketDataAsOf='20260904'
  AND CreatedUtc>@Open AND CreatedUtc<@Now)
 THROW 51431,'Exact reviewed daily source or assignment changed.',1;
IF (SELECT COUNT(*) FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 AND l.Lens IN(N'Continuation',N'Breakout')
 AND l.IsEligible=0 AND l.Direction=N'Hold' AND l.FirstFailedGate=N'Breadth')<>50
 THROW 51431,'Expected fifty unchanged Hold/Breadth published lens records.',1;
IF EXISTS(SELECT l.Lens,l.Rank FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 AND l.Lens IN(N'Continuation',N'Breakout')
 GROUP BY l.Lens,l.Rank HAVING COUNT(*)<>1)
 THROW 51431,'Duplicate daily ranks.',1;

DECLARE @Authorization nvarchar(max)=(SELECT @Trial TrialId,@Session SessionId,@Generation GenerationId,@Portfolio PortfolioId,
 @Policy PolicyVersionId,@Date EffectiveSession,@Open EffectiveSessionOpenUtc,@Now AuthorizedUtc,ORIGINAL_LOGIN() AuthorizedBy,
 CAST(1000 AS decimal(28,6)) StartingCapital,N'CAD' Currency,@Reason Reason,
 N'OperatorEngineeringTest' Admission,N'Incomplete; never a clean research cohort' Coverage,
 @Run OriginalCalibrationRunId,N'Preserved Hold/Breadth evidence' DailyEligibility,
 N'Continue normal next-session selection; retain carried test effects' Rollover
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
INSERT dbo.DelphiLiveEngineeringTrial(TrialId,TradingDate,CalibrationRunId,AuthorizedUtc,AuthorizedBy,Reason,EvidenceJson)
 VALUES(@Trial,@Date,@Run,@Now,ORIGINAL_LOGIN(),@Reason,@Authorization);
INSERT dbo.DelphiLivePolicyAssignment
 (AssignmentId,DelphiLivePolicyVersionId,PolicyRole,RoleSlot,ExperimentId,EffectiveTradingDate,AuthorizedUtc,AuthorizedBy,AuthorizationReason,DecisionRef)
 VALUES(@Assignment,@Policy,N'OperationalChampion',0,NULL,@Date,@Now,ORIGINAL_LOGIN(),@Reason,N'ADR-0065');
INSERT dbo.DelphiLivePortfolioGeneration
 (GenerationId,AssignmentId,DelphiLivePolicyVersionId,PortfolioRole,ExperimentId,StartingCapital,Currency,
 EffectiveTradingDate,EffectiveSessionOpenUtc,AuthorizedUtc,AuthorizedBy,AuthorizationReason,AuthorizationJson,EngineeringTrialId)
 VALUES(@Generation,@Assignment,@Policy,N'OperationalChampion',NULL,1000,'CAD',@Date,@Open,@Now,ORIGINAL_LOGIN(),@Reason,@Authorization,@Trial);
DECLARE @Snapshot nvarchar(max)=JSON_VALUE(@PreparedJson,'$.SnapshotJson');
IF @Snapshot IS NULL THROW 51431,'Missing prepared cash-only snapshot.',1;
SET @Snapshot=JSON_MODIFY(@Snapshot,'$.UpdatedUtc',CONVERT(varchar(33),@Now,126)+'Z');
INSERT dbo.DelphiLivePortfolioLedger(PortfolioId,GenerationId,DelphiLivePolicyVersionId,Revision,SnapshotSchemaVersion,SnapshotJson,UpdatedUtc)
 VALUES(@Portfolio,@Generation,@Policy,0,1,@Snapshot,@Now);
INSERT dbo.DelphiLivePortfolioRevision(PortfolioId,Revision,SnapshotJson) VALUES(@Portfolio,0,@Snapshot);
INSERT dbo.DelphiLiveLedgerEvent(EventId,PortfolioId,Revision,EventKind,RecordedUtc,DataJson)
 VALUES(NEWID(),@Portfolio,0,N'OperatorEngineeringActivationAuthorized',@Now,@Authorization);

INSERT dbo.DelphiLiveSession
 (SessionId,TradingDate,SessionOpenUtc,SessionCloseUtc,FreezeBoundaryUtc,FrozenUtc,ExpectedPriorCanonicalSessionDate,
 FreezeStatus,CalibrationRunId,DailyStrategyVersionId,CalibrationRunPurpose,CalibrationRunAuditState,
 CalibrationRecommendationDate,CalibrationMarketDataAsOf,CalibrationRunStartedUtc,CalibrationRunCreatedUtc,
 CollectorVersion,CollectorSourceContractVersion,CalendarVersion,CodeCommit,WorkingTreeState,SessionState,CoverageState,HostGapObserved,EngineeringTrialId)
 SELECT @Session,@Date,@Open,@Close,@Open,@Now,'20260904',N'OperatorEngineeringTest',r.RunId,r.StrategyVersionId,r.RunPurpose,
 r.AuditState,r.RecommendationDate,r.MarketDataAsOf,r.StartedUtc,r.CreatedUtc,N'IntradayEvidenceCollectorV3',1,
 JSON_VALUE(@PreparedJson,'$.Calendar'),JSON_VALUE(@PreparedJson,'$.CodeCommit'),N'Dirty',N'Frozen',N'Blocked',1,@Trial
 FROM dbo.CalibrationRun r WHERE r.RunId=@Run;
INSERT dbo.DelphiLiveSessionPolicy
 (SessionPolicyId,SessionId,AssignmentId,DelphiLivePolicyVersionId,DailyStrategyVersionId,PolicyRole,RoleSlot,
 ExperimentId,IsOperationallyEnabled,PolicySettingsJson,PolicySettingsSha256,FrozenUtc)
 SELECT NEWID(),@Session,@Assignment,@Policy,@Strategy,N'OperationalChampion',0,NULL,1,SettingsJson,SettingsSha256,@Now
 FROM dbo.DelphiLivePolicyVersion WHERE DelphiLivePolicyVersionId=@Policy;
;WITH selected AS
 (SELECT c.Symbol,COUNT(*) Lenses,MIN(l.Rank) BestRank FROM dbo.CalibrationCandidate c
 JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
 WHERE c.RunId=@Run AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 AND l.Lens IN(N'Continuation',N'Breakout') GROUP BY c.Symbol),
 held AS (SELECT Symbol FROM dbo.ActivePosition WHERE IsActive=1 UNION SELECT Symbol FROM dbo.ShadowPosition WHERE Status=N'Open'),
 required AS (SELECT Symbol FROM selected UNION SELECT Symbol FROM held UNION SELECT N'XIU')
INSERT dbo.DelphiLiveSessionSymbol
 (SessionSymbolId,SessionId,Symbol,IsFrozenDailyCandidate,IsXiuBenchmark,IsTrackedHolding,IsDelphiLiveHolding,
 HasPendingProtectiveSell,IsSessionCarryCandidate,FrozenSourceLensCount,BestFrozenSourceLensRank,
 RequiredFromBarEndUtc,RequiredThroughBarEndUtc,SourceIdentityJson,AddedUtc)
 SELECT NEWID(),@Session,r.Symbol,IIF(s.Symbol IS NULL,0,1),IIF(r.Symbol=N'XIU',1,0),IIF(h.Symbol IS NULL,0,1),
 0,0,0,COALESCE(s.Lenses,0),s.BestRank,DATEADD(minute,5,@Open),@Close,@Authorization,@Now
 FROM required r LEFT JOIN selected s ON s.Symbol=r.Symbol LEFT JOIN held h ON h.Symbol=r.Symbol;
INSERT dbo.DelphiLiveFrozenCandidate
 (FrozenCandidateId,SessionId,SessionSymbolId,CalibrationRunId,CalibrationCandidateId,DailyStrategyVersionId,
 ObservationDate,ObservationOpen,ObservationHigh,ObservationLow,ObservationClose,ObservationVolume,DirectionEdge,
 CommonCompositeScore,CandidateSnapshotSchemaVersion,CandidateSnapshotJson,CalibrationCandidateCreatedUtc,FrozenUtc)
 SELECT NEWID(),@Session,s.SessionSymbolId,@Run,c.CandidateId,@Strategy,c.ObservationDate,c.ObservationOpen,c.ObservationHigh,
 c.ObservationLow,c.ObservationClose,c.ObservationVolume,c.DirectionEdge,c.CompositeScore,c.SnapshotSchemaVersion,c.SnapshotJson,c.CreatedUtc,@Now
 FROM dbo.DelphiLiveSessionSymbol s JOIN dbo.CalibrationCandidate c ON c.RunId=@Run AND c.Symbol=s.Symbol
 WHERE s.SessionId=@Session AND s.IsFrozenDailyCandidate=1;
INSERT dbo.DelphiLiveFrozenCandidateLens
 (FrozenCandidateLensId,FrozenCandidateId,CalibrationCandidateId,CalibrationLensEvaluationId,Lens,Direction,IsEligible,
 FrozenRank,FrozenRankingKey,IsPublished,FirstFailedGate,TraceSchemaVersion,GateTraceJson,CalibrationLensCreatedUtc,FrozenUtc,EngineeringTrialId)
 SELECT NEWID(),f.FrozenCandidateId,f.CalibrationCandidateId,l.LensEvaluationId,l.Lens,l.Direction,l.IsEligible,l.Rank,
 l.RankingKey,l.IsPublished,l.FirstFailedGate,l.TraceSchemaVersion,l.GateTraceJson,l.CreatedUtc,@Now,@Trial
 FROM dbo.DelphiLiveFrozenCandidate f JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=f.CalibrationCandidateId
 WHERE f.SessionId=@Session AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND 25 AND l.Lens IN(N'Continuation',N'Breakout');
INSERT dbo.DelphiLiveDailyBaseline
 (DailyBaselineId,SessionId,SessionSymbolId,BaselineDefinition,BaselineSchemaVersion,SourceThroughTradingDate,
 AlignedDailyBarCount,PreviousCanonicalClose,MedianTrueRangePct5,MedianTrueRangePct10,MedianTrueRangePct14,
 MedianTrueRangePct20,MedianFullDayVolume20,AlignedDailyBarsJson,AuditState,AuditCode,FrozenUtc)
 SELECT NEWID(),@Session,s.SessionSymbolId,N'DelphiLiveDailyBaselineV1',1,'20260904',b.Count,b.Previous,b.R5,b.R10,b.R14,b.R20,
 b.Volume,b.BaselineJson,b.Audit,b.Reason,@Now FROM OPENJSON(@PreparedJson,'$.Baselines')
 WITH(Symbol nvarchar(20),Count int,Previous decimal(19,6),R5 decimal(28,12),R10 decimal(28,12),
 R14 decimal(28,12),R20 decimal(28,12),Volume decimal(28,6),BaselineJson nvarchar(max),Audit nvarchar(16),Reason nvarchar(64)) b
 JOIN dbo.DelphiLiveSessionSymbol s ON s.SessionId=@Session AND s.Symbol=b.Symbol;
IF (SELECT COUNT(*) FROM dbo.DelphiLiveFrozenCandidate WHERE SessionId=@Session)<>38
 OR (SELECT COUNT(*) FROM dbo.DelphiLiveFrozenCandidateLens WHERE EngineeringTrialId=@Trial)<>50
 OR EXISTS(SELECT 1 FROM dbo.DelphiLiveSessionSymbol s LEFT JOIN dbo.DelphiLiveDailyBaseline b ON b.SessionSymbolId=s.SessionSymbolId
  WHERE s.SessionId=@Session AND b.DailyBaselineId IS NULL)
 THROW 51431,'Candidate or baseline coverage failed.',1;
IF EXISTS(SELECT 1 FROM dbo.DelphiLiveFrozenCandidateLens f JOIN dbo.CalibrationLensEvaluation l ON l.LensEvaluationId=f.CalibrationLensEvaluationId
 WHERE f.EngineeringTrialId=@Trial AND (f.IsEligible<>l.IsEligible OR f.Direction<>l.Direction OR f.FirstFailedGate<>l.FirstFailedGate OR f.GateTraceJson<>l.GateTraceJson))
 THROW 51431,'Original daily evidence was not preserved.',1;

