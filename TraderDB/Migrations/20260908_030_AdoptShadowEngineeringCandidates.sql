/*
Purpose: ADR-0064, operator-requested September 8 engineering test. Admit the
         existing official report's published Top 3/5 into the four current
         Shadow sessions despite the late report and failed daily eligibility.
Preconditions: exact reviewed run; four active Ghost portfolios; today's four
               empty NoValidDelphiRun sessions; no pause/risk hold or pending order.
Effects: update four operational sessions and four display names; insert 16
         candidate rows and four append-only override events. No schema change,
         position/order/capital change, calibration rewrite or synthetic fill.
Rehearsal: sqlcmd ... -v ApplyOverride=0 (always rolls back).
Apply:     sqlcmd ... -v ApplyOverride=1 after a fresh verified backup.
Recovery: transaction rollback on any failure. After commit, pause new Shadow
          risk through the normal app control if needed; retain observations,
          events, holdings and protective exits. Do not delete this day's history
          or restore a whole backup over subsequent trades. Any later reversal
          needs a separate reviewed operation. This script is single-use.
*/
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 15000;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

DECLARE @Apply bit = TRY_CONVERT(bit, N'$(ApplyOverride)');
IF N'$(ApplyOverride)' NOT IN (N'0',N'1') OR @Apply IS NULL
    THROW 51430, 'Supply ApplyOverride=0 for rehearsal or 1 for authorized application.', 1;
IF DB_NAME() <> N'TraderDB' THROW 51430, 'Unexpected database.', 1;
DECLARE @Date date = '20260908';
DECLARE @RunId uniqueidentifier = '9491e421-ef3e-48b4-bcb7-389c42ec25e9';
DECLARE @StrategyId uniqueidentifier = '48864449-92f4-4e33-99cf-40039210786e';
DECLARE @TrialId uniqueidentifier = '3a36557d-c4c4-45ca-a9b1-ab86e046d69a';
DECLARE @Now datetime2, @Baseline datetime2, @LockResult int;
DECLARE @BeforeEventCount bigint, @PortfolioFinancialBefore varbinary(32), @PortfolioFinancialAfter varbinary(32);
DECLARE @SchemaBefore varbinary(32), @SchemaAfter varbinary(32);
DECLARE @Targets table
(
    PortfolioId uniqueidentifier PRIMARY KEY, SessionId uniqueidentifier UNIQUE,
    PortfolioCode nvarchar(32), Lens nvarchar(16), MaximumPositions tinyint,
    OldDisplayName nvarchar(128), OriginalSessionJson nvarchar(max), EventId uniqueidentifier
);
DECLARE @Protected table
(
    TableName sysname PRIMARY KEY, KeyColumn sysname, BeforeHash varbinary(32), AfterHash varbinary(32)
);
INSERT @Protected(TableName,KeyColumn) VALUES
 (N'ShadowPortfolioGeneration',N'GenerationId'),(N'ShadowPosition',N'PositionId'),
 (N'ShadowOrder',N'OrderId'),(N'ShadowCapitalEvent',N'CapitalEventId'),
 (N'ActivePosition',N'PositionId'),(N'TradeLog',N'TradeId'),
 (N'TradingReportedFill',N'RequestId'),(N'TradingSystemControl',N'SystemKey'),
 (N'EngineStrategyAssignment',N'AssignmentId'),(N'StrategyVersion',N'VersionId'),
 (N'CalibrationRun',N'RunId'),(N'CalibrationCandidate',N'CandidateId'),
 (N'CalibrationLensEvaluation',N'LensEvaluationId'),(N'DailyPick',N'PickId');

BEGIN TRY
    BEGIN TRANSACTION;
    -- Matches the running WPF controller's shared lease: wait for its current
    -- cycle to finish, then prevent another from observing partial adoption.
    EXEC @LockResult=sys.sp_getapplock @Resource=N'TraderVI.EngineSettings',
        @LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
    IF @LockResult < 0 THROW 51430, 'Could not fence the running monitor; no override applied.', 1;
    EXEC @LockResult=sys.sp_getapplock @Resource=N'TraderVI.StrategyModelSelection',
        @LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=15000;
    IF @LockResult < 0 THROW 51430, 'Daily strategy selection is busy; no override applied.', 1;
    SET @Now=SYSUTCDATETIME();
    IF @Now < '2026-09-08T13:30:00' OR @Now >= '2026-09-08T19:30:00'
        THROW 51430, 'This reviewed September 8 operation is outside its activation window.', 1;
    -- The runtime requires latest bar END > baseline. Set baseline to the next
    -- five-minute boundary so the eligible bar START is after actual adoption.
    SET @Baseline=DATEADD(minute,(DATEDIFF(minute,CONVERT(datetime2,'20000101'),@Now)/5+1)*5,
        CONVERT(datetime2,'20000101'));
    IF EXISTS(SELECT 1 FROM dbo.ShadowPortfolioEvent
        WHERE ReasonCode=N'OperatorForcedTopNTest' AND JSON_VALUE(DetailsJson,'$.trialId')=CONVERT(nvarchar(36),@TrialId))
        THROW 51430, 'This engineering override has already been applied; do not retry.', 1;
    IF (SELECT COUNT(*) FROM dbo.StrategyVersion WHERE IsActive=1)<>1
        OR NOT EXISTS(SELECT 1 FROM dbo.StrategyVersion WHERE VersionId=@StrategyId AND IsActive=1)
        THROW 51430, 'The active daily strategy changed.', 1;
    IF NOT EXISTS(SELECT 1 FROM dbo.CalibrationRun WHERE RunId=@RunId
        AND RecommendationDate=@Date AND MarketDataAsOf='20260904' AND RunPurpose=N'OfficialPaper'
        AND AuditState=N'Valid' AND StrategyVersionId=@StrategyId
        AND CreatedUtc>'2026-09-08T13:30:00' AND CreatedUtc<@Now)
        THROW 51430, 'The exact reviewed late official run is unavailable.', 1;
    IF EXISTS(SELECT 1 FROM dbo.TradingSystemControl WHERE SystemKey IN (N'Daily',N'SystemShadow') AND Paused=1)
        THROW 51430, 'A durable entry pause is active; this script never clears pauses.', 1;
    IF (SELECT COUNT(*) FROM dbo.ShadowPortfolioGeneration WHERE Status<>N'Stopped')<>1
        THROW 51430, 'Expected one current Shadow generation.', 1;

    INSERT @Targets(PortfolioId,SessionId,PortfolioCode,Lens,MaximumPositions,OldDisplayName,OriginalSessionJson,EventId)
    SELECT p.PortfolioId,s.SessionId,p.PortfolioCode,p.Lens,p.MaximumPositions,p.DisplayName,
        (SELECT old.* FROM dbo.ShadowPortfolioSession old WHERE old.SessionId=s.SessionId
         FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES),NEWID()
    FROM dbo.ShadowPortfolio p JOIN dbo.ShadowPortfolioGeneration g ON g.GenerationId=p.GenerationId
    JOIN dbo.ShadowPortfolioSession s ON s.PortfolioId=p.PortfolioId AND s.TradingDate=@Date
    WHERE g.Status=N'Active' AND p.Status=N'Active' AND p.SelectionActor=N'System' AND p.ExecutionMode=N'Ghost'
        AND s.Status=N'NoValidDelphiRun' AND s.CalibrationRunId IS NULL AND s.DailyLossGuardActive=0
        AND s.CompletedUtc IS NULL AND s.ClosingValue IS NULL;
    IF (SELECT COUNT(*) FROM @Targets)<>4 OR (SELECT SUM(CONVERT(int,MaximumPositions)) FROM @Targets)<>16
        OR (SELECT COUNT(DISTINCT PortfolioCode) FROM @Targets)<>4
        THROW 51430, 'Expected the four active, unlinked and unguarded September 8 sessions.', 1;
    IF EXISTS(SELECT 1 FROM @Targets WHERE LEN(OldDisplayName)>100 OR OldDisplayName LIKE N'%[[]TEST%')
        THROW 51430, 'Review existing names before adding the test label.', 1;
    IF EXISTS(SELECT 1 FROM dbo.ShadowPortfolioCandidate c JOIN @Targets t ON t.SessionId=c.SessionId)
        THROW 51430, 'A target session already has candidates; do not replace them.', 1;
    IF EXISTS(SELECT 1 FROM dbo.ShadowOrder o JOIN @Targets t ON t.PortfolioId=o.PortfolioId
        WHERE o.Status=N'Pending' OR o.SessionId=t.SessionId)
        THROW 51430, 'A target has pending orders or an existing same-day order; review before proceeding.', 1;
    IF EXISTS(SELECT 1 FROM @Targets t OUTER APPLY
        (SELECT COUNT(*) n,COUNT(DISTINCT l.Rank) ranks,MIN(l.Rank) lowRank,MAX(l.Rank) highRank
         FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
         WHERE c.RunId=@RunId AND l.Lens=t.Lens AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND t.MaximumPositions
             AND l.IsEligible=0 AND l.Direction=N'Hold' AND l.FirstFailedGate=N'Breadth'
             AND c.ObservationDate='20260904' AND c.ObservationClose>0) x
        WHERE x.n<>t.MaximumPositions OR x.ranks<>t.MaximumPositions OR x.lowRank<>1 OR x.highRank<>t.MaximumPositions)
        THROW 51430, 'Reviewed Top-N rank, Hold/Breadth or source-price preconditions changed.', 1;

    SET @BeforeEventCount=(SELECT COUNT_BIG(*) FROM dbo.ShadowPortfolioEvent);
    SET @PortfolioFinancialBefore=HASHBYTES('SHA2_256',(SELECT PortfolioId,GenerationId,PortfolioCode,Lens,
        MaximumPositions,SelectionActor,ExecutionMode,Status,CashBalance,HighestClosingValue,PauseReason,CreatedUtc
        FROM dbo.ShadowPortfolio ORDER BY PortfolioId FOR JSON PATH,INCLUDE_NULL_VALUES));
    SET @SchemaBefore=HASHBYTES('SHA2_256',(SELECT object_id,name,type,modify_date FROM sys.objects
        WHERE is_ms_shipped=0 ORDER BY object_id FOR JSON PATH,INCLUDE_NULL_VALUES));
    DECLARE @Pass int=0, @Table sysname, @Key sysname, @Sql nvarchar(max), @Hash varbinary(32);
    WHILE @Pass<2
    BEGIN
        DECLARE protected_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,KeyColumn FROM @Protected ORDER BY TableName;
        OPEN protected_cursor;
        FETCH NEXT FROM protected_cursor INTO @Table,@Key;
        WHILE @@FETCH_STATUS=0
        BEGIN
            SET @Sql=N'SELECT @h=HASHBYTES(''SHA2_256'',COALESCE((SELECT * FROM dbo.'+QUOTENAME(@Table)
                +N' ORDER BY '+QUOTENAME(@Key)+N' FOR JSON PATH,INCLUDE_NULL_VALUES),N''[]''));';
            EXEC sys.sp_executesql @Sql,N'@h varbinary(32) OUTPUT',@h=@Hash OUTPUT;
            IF @Pass=0 UPDATE @Protected SET BeforeHash=@Hash WHERE TableName=@Table;
            ELSE UPDATE @Protected SET AfterHash=@Hash WHERE TableName=@Table;
            FETCH NEXT FROM protected_cursor INTO @Table,@Key;
        END;
        CLOSE protected_cursor;
        DEALLOCATE protected_cursor;
        IF @Pass=0
        BEGIN
            INSERT dbo.ShadowPortfolioEvent(EventId,PortfolioId,SessionId,OccurredUtc,EventType,ReasonCode,DetailsJson)
            SELECT t.EventId,t.PortfolioId,t.SessionId,@Now,N'Lifecycle',N'OperatorForcedTopNTest',
                (SELECT CONVERT(nvarchar(36),@TrialId) trialId,N'ADR-0064' decisionRef,
                    N'Operator requested published Top 3/5 candidates for a same-day paper engineering test.' operatorReason,
                    @Date tradingDate,@Now adoptedUtc,@Baseline activationBaselineUtc,@RunId adoptedRunId,
                    CONVERT(bit,1) bypassPreOpenCutoff,CONVERT(bit,1) bypassDailyEligibility,
                    CONVERT(bit,1) preserveIntradayEntryAndRiskRules,N'EngineeringOnly' evidenceUse,
                    N'Raw portfolio charts still include this test and carried effects.' comparisonLimitation,
                    t.OldDisplayName priorDisplayName,JSON_QUERY(t.OriginalSessionJson) priorSession,
                    JSON_QUERY((SELECT c.CandidateId,l.Rank,l.Direction,l.IsEligible,l.FirstFailedGate
                        FROM dbo.CalibrationCandidate c JOIN dbo.CalibrationLensEvaluation l ON l.CandidateId=c.CandidateId
                        WHERE c.RunId=@RunId AND l.Lens=t.Lens AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND t.MaximumPositions
                        ORDER BY l.Rank FOR JSON PATH)) admittedCandidates
                 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES)
            FROM @Targets t;
            IF @@ROWCOUNT<>4 THROW 51430, 'Expected exactly four override events.', 1;
            UPDATE s SET CalibrationRunId=@RunId,Status=N'Active',ActivationBaselineUtc=@Baseline,UpdatedUtc=@Now
            FROM dbo.ShadowPortfolioSession s JOIN @Targets t ON t.SessionId=s.SessionId;
            IF @@ROWCOUNT<>4 THROW 51430, 'Expected exactly four session adoptions.', 1;
            INSERT dbo.ShadowPortfolioCandidate(CandidateTrackingId,SessionId,CalibrationCandidateId,Symbol,Rank,
                State,ReasonCode,LastEvaluatedUtc,CreatedUtc,UpdatedUtc)
            SELECT NEWID(),t.SessionId,c.CandidateId,c.Symbol,l.Rank,N'Pending',N'OperatorForcedTopNTest',NULL,@Now,@Now
            FROM @Targets t JOIN dbo.CalibrationLensEvaluation l ON l.Lens=t.Lens
                AND l.IsPublished=1 AND l.Rank BETWEEN 1 AND t.MaximumPositions
            JOIN dbo.CalibrationCandidate c ON c.CandidateId=l.CandidateId AND c.RunId=@RunId;
            IF @@ROWCOUNT<>16 THROW 51430, 'Expected exactly sixteen candidate assignments.', 1;
            UPDATE p SET DisplayName=t.OldDisplayName+N' [TEST 2026-09-08]',UpdatedUtc=@Now
            FROM dbo.ShadowPortfolio p JOIN @Targets t ON t.PortfolioId=p.PortfolioId;
            IF @@ROWCOUNT<>4 THROW 51430, 'Expected exactly four visible test labels.', 1;
        END;
        SET @Pass+=1;
    END;
    IF EXISTS(SELECT 1 FROM @Protected WHERE BeforeHash<>AfterHash OR BeforeHash IS NULL OR AfterHash IS NULL)
        THROW 51430, 'Protected financial, control or official evidence rows changed.', 1;
    SET @PortfolioFinancialAfter=HASHBYTES('SHA2_256',(SELECT PortfolioId,GenerationId,PortfolioCode,Lens,
        MaximumPositions,SelectionActor,ExecutionMode,Status,CashBalance,HighestClosingValue,PauseReason,CreatedUtc
        FROM dbo.ShadowPortfolio ORDER BY PortfolioId FOR JSON PATH,INCLUDE_NULL_VALUES));
    IF @PortfolioFinancialBefore<>@PortfolioFinancialAfter
        THROW 51430, 'Portfolio cash, identity, status or risk state changed.', 1;
    SET @SchemaAfter=HASHBYTES('SHA2_256',(SELECT object_id,name,type,modify_date FROM sys.objects
        WHERE is_ms_shipped=0 ORDER BY object_id FOR JSON PATH,INCLUDE_NULL_VALUES));
    IF @SchemaBefore<>@SchemaAfter THROW 51430, 'Unexpected schema change.', 1;
    IF (SELECT COUNT_BIG(*) FROM dbo.ShadowPortfolioEvent)<>@BeforeEventCount+4
        THROW 51430, 'Unexpected event-ledger count change.', 1;
    IF EXISTS(SELECT 1 FROM @Targets t JOIN dbo.ShadowPortfolioSession s ON s.SessionId=t.SessionId
        OUTER APPLY(SELECT COUNT(*) n FROM dbo.ShadowPortfolioCandidate c WHERE c.SessionId=t.SessionId) x
        WHERE s.Status<>N'Active' OR s.CalibrationRunId<>@RunId OR s.ActivationBaselineUtc<>@Baseline
            OR x.n<>t.MaximumPositions OR s.DailyLossGuardActive<>0)
        THROW 51430, 'Session or candidate postconditions failed.', 1;

    IF @Apply=1 COMMIT TRANSACTION;
    ELSE ROLLBACK TRANSACTION;
    SELECT CASE WHEN @Apply=1 THEN N'Applied' ELSE N'RehearsedAndRolledBack' END Result,
        @TrialId TrialId,@Now CheckedUtc,@Baseline FirstPermittedBarStartUtc,
        4 TargetPortfolios,16 AdmittedCandidateAssignments,
        (SELECT COUNT(*) FROM @Protected) ProtectedTableHashesMatched,
        CONVERT(bit,1) PortfolioFinancialStatePreserved,CONVERT(bit,1) SchemaPreserved;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local','protected_cursor')>=0 CLOSE protected_cursor;
    IF CURSOR_STATUS('local','protected_cursor')>=-1 DEALLOCATE protected_cursor;
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
