#nullable enable

namespace Core.Db;

public sealed partial class DelphiLiveCollectionRepository
{
    private const string AcquireLeaseSql = """
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
IF @Expiry <= @Now THROW 52201, 'Lease expiry has already elapsed.', 1;
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveHostLease WITH (UPDLOCK,HOLDLOCK)
           WHERE LeaseName=N'DelphiLiveMonitor' AND IsHeld=1 AND ExpiresUtc>@Now)
BEGIN
    SELECT LeaseId,OwnerId,FencingToken,AcquiredUtc,ExpiresUtc FROM dbo.DelphiLiveHostLease WHERE 1=0;
    RETURN;
END;
UPDATE dbo.DelphiLiveHostLease SET IsHeld=0,LeaseLostUtc=@Now
WHERE LeaseName=N'DelphiLiveMonitor' AND IsHeld=1 AND ExpiresUtc<=@Now;
DECLARE @Token BIGINT=(SELECT ISNULL(MAX(FencingToken),0)+1 FROM dbo.DelphiLiveHostLease WITH (UPDLOCK,HOLDLOCK)
                      WHERE LeaseName=N'DelphiLiveMonitor');
DECLARE @Id UNIQUEIDENTIFIER=NEWID();
INSERT dbo.DelphiLiveHostLease
    (LeaseId,LeaseName,OwnerId,FencingToken,CollectorVersion,SourceContractVersion,
     CodeCommit,WorkingTreeState,AcquiredUtc,LastRenewedUtc,ExpiresUtc,IsHeld)
VALUES (@Id,N'DelphiLiveMonitor',@Owner,@Token,N'IntradayEvidenceCollectorV3',1,@Code,@Tree,@Now,@Now,@Expiry,1);
SELECT LeaseId,OwnerId,FencingToken,AcquiredUtc,ExpiresUtc FROM dbo.DelphiLiveHostLease WHERE LeaseId=@Id;
""";

    private const string RenewLeaseSql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME();
UPDATE dbo.DelphiLiveHostLease WITH (UPDLOCK,HOLDLOCK)
SET LastRenewedUtc=@Now,ExpiresUtc=CASE WHEN ExpiresUtc>@Expiry THEN ExpiresUtc ELSE @Expiry END
WHERE LeaseId=@LeaseId AND OwnerId=@Owner AND FencingToken=@Fence
  AND IsHeld=1 AND ExpiresUtc>@Now AND @Expiry>@Now AND LastRenewedUtc<=@Now;
SELECT CAST(CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END AS BIT);
""";

    private const string ReleaseLeaseSql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME();
UPDATE dbo.DelphiLiveHostLease WITH (UPDLOCK,HOLDLOCK)
SET IsHeld=0,ReleasedUtc=@Now
WHERE LeaseId=@LeaseId AND OwnerId=@Owner AND FencingToken=@Fence AND IsHeld=1;
IF @@ROWCOUNT=0 RETURN;
UPDATE s SET Disposition=N'CollectionFailed',DispositionCode=N'HostStopped',OperationallyUsable=0,
    MissedOperationalDeadline=1,SettledUtc=@Now,UpdatedUtc=@Now
FROM dbo.IntradayCollectionSlot s JOIN dbo.IntradayCollectionCycle c ON c.CycleId=s.CycleId
WHERE c.LeaseId=@LeaseId AND c.LeaseFencingToken=@Fence AND s.Disposition=N'Pending';
UPDATE dbo.IntradayCollectionCycle SET CycleStatus=CASE WHEN DeadlineUtc<=@Now THEN N'DeadlineExceeded' ELSE N'Cancelled' END,
    CompletionCode=N'HostStopped',CompletedUtc=@Now,UpdatedUtc=@Now,SettledSlotCount=ExpectedSlotCount
WHERE LeaseId=@LeaseId AND LeaseFencingToken=@Fence AND CycleStatus IN (N'Planned',N'Collecting');
UPDATE e SET EndedUtc=@Now,
    EndReason=CASE WHEN @Now>=DATEADD(MINUTE,2,s.SessionCloseUtc) THEN N'SessionClose' ELSE N'HostStopped' END,
    CoverageDisposition=CASE WHEN @Now>=DATEADD(MINUTE,2,s.SessionCloseUtc) THEN N'Complete' ELSE N'StoppedEarly' END,
    HostGapObserved=CASE WHEN @Now<DATEADD(MINUTE,2,s.SessionCloseUtc) THEN 1 ELSE e.HostGapObserved END
FROM dbo.DelphiLiveContinuityEpoch e JOIN dbo.DelphiLiveSession s ON s.SessionId=e.SessionId
WHERE e.LeaseId=@LeaseId AND e.LeaseFencingToken=@Fence AND e.EndedUtc IS NULL;
UPDATE s SET HostGapObserved=1,CoverageState=N'Blocked',UpdatedUtc=@Now
FROM dbo.DelphiLiveSession s WHERE EXISTS
 (SELECT 1 FROM dbo.DelphiLiveContinuityEpoch e WHERE e.SessionId=s.SessionId AND e.LeaseId=@LeaseId
  AND e.HostGapObserved=1);
""";

    private const string AssertLeaseSql = """
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveHostLease WITH (UPDLOCK,HOLDLOCK)
    WHERE LeaseId=@LeaseId AND OwnerId=@Owner AND FencingToken=@Fence AND IsHeld=1 AND ExpiresUtc>@Now)
    THROW 52202, 'Delphi Live lease lost or expired.', 1;
""";

    private const string RecoverSessionSql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME();
""" + AssertLeaseSql + """

DECLARE @Open DATETIME2,@Close DATETIME2;
SELECT @Open=SessionOpenUtc,@Close=SessionCloseUtc FROM dbo.DelphiLiveSession WITH (UPDLOCK,HOLDLOCK)
WHERE SessionId=@SessionId;
IF @Open IS NULL THROW 52203, 'Freeze the Delphi Live session before recovery.', 1;
IF @Now<@Open THROW 52204, 'Recovery cannot begin before the session boundary.', 1;
-- Finalization is idempotent. Once the complete through-close grid has been
-- settled, another stop/tick must not manufacture a restart gap or new epoch.
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveSession WHERE SessionId=@SessionId
           AND SessionState IN(N'Completed',N'Incomplete') AND CompletedUtc>=SessionCloseUtc)
BEGIN
    SELECT TOP(1) ContinuityEpochId,EpochNumber,HostGapObserved,CAST(0 AS INT),@Now
    FROM dbo.DelphiLiveContinuityEpoch WHERE SessionId=@SessionId ORDER BY EpochNumber DESC;
    RETURN;
END;
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@SessionId AND IsXiuBenchmark=1)
    THROW 52205, 'Recovery requires the complete expected observation membership including XIU.', 1;

-- Abandoned work stays a missed operational fact even if historical bars arrive later.
UPDATE sl SET Disposition=N'CycleDeadlineExceeded',DispositionCode=N'HostCoverageGap',
    OperationallyUsable=0,MissedOperationalDeadline=1,SettledUtc=@Now,UpdatedUtc=@Now
FROM dbo.IntradayCollectionSlot sl JOIN dbo.IntradayCollectionCycle c ON c.CycleId=sl.CycleId
WHERE c.CycleStatus IN (N'Planned',N'Collecting') AND sl.Disposition=N'Pending'
  AND (c.DeadlineUtc<=@Now OR c.LeaseId<>@LeaseId);
UPDATE c SET CycleStatus=CASE WHEN c.DeadlineUtc<=@Now THEN N'DeadlineExceeded' ELSE N'Cancelled' END,
    CompletionCode=N'HostCoverageGap',CompletedUtc=@Now,UpdatedUtc=@Now,SettledSlotCount=ExpectedSlotCount
FROM dbo.IntradayCollectionCycle c
WHERE c.CycleStatus IN (N'Planned',N'Collecting') AND (c.DeadlineUtc<=@Now OR c.LeaseId<>@LeaseId);
UPDATE e SET EndedUtc=@Now,EndReason=N'LeaseLost',CoverageDisposition=N'LeaseLost',HostGapObserved=1
FROM dbo.DelphiLiveContinuityEpoch e
WHERE e.EndedUtc IS NULL AND e.LeaseId<>@LeaseId;

DECLARE @EpochId UNIQUEIDENTIFIER,@Epoch INT,@Gap BIT=0,@Previous UNIQUEIDENTIFIER;
SELECT @EpochId=ContinuityEpochId,@Epoch=EpochNumber,@Gap=HostGapObserved
FROM dbo.DelphiLiveContinuityEpoch WITH (UPDLOCK,HOLDLOCK)
WHERE SessionId=@SessionId AND LeaseId=@LeaseId AND LeaseFencingToken=@Fence AND EndedUtc IS NULL;
IF @EpochId IS NULL
BEGIN
    SELECT TOP (1) @Previous=ContinuityEpochId,@Epoch=EpochNumber
    FROM dbo.DelphiLiveContinuityEpoch WHERE SessionId=@SessionId ORDER BY EpochNumber DESC;
    SET @Epoch=ISNULL(@Epoch,0)+1;
    SET @EpochId=NEWID();
    -- Only host provenance can establish opening continuity. An initial call
    -- before the first poll is still a late start when the host was not armed
    -- at the session boundary; an existing epoch always makes this a restart.
    SET @Gap=CASE WHEN @Previous IS NOT NULL OR @WasArmedAtSessionOpen=0 THEN 1 ELSE 0 END;
    INSERT dbo.DelphiLiveContinuityEpoch
      (ContinuityEpochId,SessionId,EpochNumber,PreviousContinuityEpochId,LeaseId,LeaseOwnerId,
       LeaseFencingToken,BeganAtSessionOpen,StartReason,StartedUtc,OperationalBuffersResetUtc,
       RestartDispositionJson,CoverageDisposition,HostGapObserved)
    VALUES (@EpochId,@SessionId,@Epoch,@Previous,@LeaseId,@Owner,@Fence,
      CASE WHEN @Gap=0 THEN 1 ELSE 0 END,
      CASE WHEN @Previous IS NOT NULL THEN N'HostRestart' WHEN @Gap=1 THEN N'LateHostStart' ELSE N'SessionOpen' END,
      @Now,@Now,N'{"operationalBuffers":"Reset","ordinaryConfirmation":"Reset","portfolioRecovery":"RequiredByHost"}',
      N'Pending',@Gap);
END;

DECLARE @Added INT=0,@End DATETIME2=DATEADD(MINUTE,5,@Open),@Cycle UNIQUEIDENTIFIER;
-- On resume the next eligible scheduled cycle is used. Every earlier start is
-- retained as a missed slot, including a still-open deadline skipped on resume.
WHILE @End<=@Close AND DATEADD(MINUTE,2,@End)<@Now
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.IntradayCollectionCycle WITH (UPDLOCK,HOLDLOCK)
                   WHERE SessionId=@SessionId AND BarEndUtc=@End)
    BEGIN
        SET @Cycle=NEWID();
        INSERT dbo.IntradayCollectionCycle
          (CycleId,SessionId,ContinuityEpochId,LeaseId,LeaseFencingToken,CollectionPurpose,Provider,
           CollectorVersion,SourceContractVersion,IntervalMinutes,BarStartUtc,BarEndUtc,ScheduledStartUtc,
           DeadlineUtc,CycleStatus,ExpectedSlotCount,SettledSlotCount,CompletedUtc,CompletionCode)
        SELECT @Cycle,@SessionId,@EpochId,@LeaseId,@Fence,N'DelphiLiveShared',N'TMXMoney',
           N'IntradayEvidenceCollectorV3',1,5,DATEADD(MINUTE,-5,@End),@End,DATEADD(MINUTE,2,@End),
           DATEADD(MINUTE,7,@End),CASE WHEN DATEADD(MINUTE,7,@End)<=@Now THEN N'DeadlineExceeded' ELSE N'Cancelled' END,
           COUNT(*),COUNT(*),@Now,N'HostCoverageGap'
        FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@SessionId
           AND RequiredFromBarEndUtc<=@End AND RequiredThroughBarEndUtc>=@End;
        INSERT dbo.IntradayCollectionSlot
          (CollectionSlotId,CycleId,SessionId,SessionSymbolId,Symbol,IntervalMinutes,ExpectedBarStartUtc,
           ExpectedBarEndUtc,ScheduledStartUtc,DeadlineUtc,IsXiuBenchmark,PriorityClass,PriorityOrdinal,
           RequiredByJson,RequestAttemptCount,Disposition,DispositionCode,OperationallyUsable,
           MissedOperationalDeadline,SettledUtc)
        SELECT NEWID(),@Cycle,@SessionId,SessionSymbolId,Symbol,5,DATEADD(MINUTE,-5,@End),@End,
          DATEADD(MINUTE,2,@End),DATEADD(MINUTE,7,@End),IsXiuBenchmark,
          CASE WHEN IsXiuBenchmark=1 THEN N'XiuBenchmark' WHEN HasPendingProtectiveSell=1 THEN N'PendingProtectiveSell'
               WHEN IsTrackedHolding=1 OR IsDelphiLiveHolding=1 THEN N'HeldSymbol' ELSE N'QuietOrDismissedCandidate' END,
          ROW_NUMBER() OVER (ORDER BY Symbol),SourceIdentityJson,0,N'CycleDeadlineExceeded',N'HostCoverageGap',0,1,@Now
        FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@SessionId
          AND RequiredFromBarEndUtc<=@End AND RequiredThroughBarEndUtc>=@End;
        SET @Added=@Added+1;
    END;
    SET @End=DATEADD(MINUTE,5,@End);
END;
IF @Added>0 SET @Gap=1;
UPDATE dbo.DelphiLiveContinuityEpoch SET HostGapObserved=CASE WHEN @Gap=1 THEN 1 ELSE HostGapObserved END
WHERE ContinuityEpochId=@EpochId;
UPDATE dbo.DelphiLiveSession SET HostGapObserved=CASE WHEN @Gap=1 THEN 1 ELSE HostGapObserved END,
    CoverageState=CASE WHEN @Gap=1 THEN N'Blocked' ELSE CoverageState END,
    SessionState=CASE WHEN @Now<DATEADD(MINUTE,7,@Close) THEN N'Monitoring' ELSE SessionState END,
    CompletedUtc=CASE WHEN @Now<DATEADD(MINUTE,7,@Close) THEN NULL ELSE CompletedUtc END,UpdatedUtc=@Now
WHERE SessionId=@SessionId;
SELECT @EpochId,@Epoch,@Gap,@Added,@Now;
""";

    private const string FinishSessionSql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME();
""" + AssertLeaseSql + """

DECLARE @Close DATETIME2,@Gap BIT,@Expected BIGINT,@Observed BIGINT,@Usable BIGINT,@Early BIT;
IF EXISTS (SELECT 1 FROM dbo.DelphiLiveSession WHERE SessionId=@SessionId
           AND SessionState IN(N'Completed',N'Incomplete') AND CompletedUtc>=SessionCloseUtc)
    RETURN;
SELECT @Close=SessionCloseUtc,@Gap=HostGapObserved FROM dbo.DelphiLiveSession WITH(UPDLOCK,HOLDLOCK)
WHERE SessionId=@SessionId;
SET @Early=CASE WHEN @Now<DATEADD(MINUTE,2,@Close) THEN 1 ELSE 0 END;
IF @Early=1 AND @HostStopping=0 THROW 52223,'Only a stopped host may finish before the closing collection.',1;
UPDATE s SET Disposition=N'CollectionFailed',DispositionCode=N'HostStopped',OperationallyUsable=0,
 MissedOperationalDeadline=1,SettledUtc=@Now,UpdatedUtc=@Now
FROM dbo.IntradayCollectionSlot s JOIN dbo.IntradayCollectionCycle c ON c.CycleId=s.CycleId
WHERE c.SessionId=@SessionId AND s.Disposition=N'Pending';
UPDATE dbo.IntradayCollectionCycle SET CycleStatus=CASE WHEN DeadlineUtc<=@Now THEN N'DeadlineExceeded' ELSE N'Cancelled' END,
 CompletionCode=N'SessionFinishedWithMisses',CompletedUtc=@Now,SettledSlotCount=ExpectedSlotCount,UpdatedUtc=@Now
WHERE SessionId=@SessionId AND CycleStatus IN(N'Planned',N'Collecting');
SELECT @Expected=COALESCE(SUM(CONVERT(BIGINT,DATEDIFF(MINUTE,RequiredFromBarEndUtc,RequiredThroughBarEndUtc)/5+1)),0)
FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@SessionId;
SELECT @Observed=COUNT_BIG(*),@Usable=COALESCE(SUM(CONVERT(BIGINT,
 CASE WHEN c.CollectionSlotId IS NULL THEN s.OperationallyUsable ELSE 0 END)),0)
FROM dbo.IntradayCollectionSlot s
LEFT JOIN (SELECT DISTINCT CollectionSlotId FROM dbo.IntradayEvidenceConflict) c ON c.CollectionSlotId=s.CollectionSlotId
WHERE s.SessionId=@SessionId;
IF @Early=1 OR @Observed<@Expected SET @Gap=1;
UPDATE dbo.DelphiLiveSession SET HostGapObserved=@Gap,SessionState=
 CASE WHEN @Gap=0 AND @Usable=@Expected THEN N'Completed' ELSE N'Incomplete' END,
 CoverageState=CASE WHEN @Gap=1 OR @Expected=0 OR @Usable*100<@Expected*95 THEN N'Blocked'
   WHEN @Usable=@Expected THEN N'Ready' ELSE N'Degraded' END,CompletedUtc=@Now,UpdatedUtc=@Now
WHERE SessionId=@SessionId;
UPDATE dbo.DelphiLiveContinuityEpoch SET EndedUtc=@Now,
 EndReason=CASE WHEN @Early=1 THEN N'HostStopped' WHEN @Gap=1 THEN N'HostGap' ELSE N'SessionClose' END,
 CoverageDisposition=CASE WHEN @Early=1 THEN N'StoppedEarly' WHEN @Gap=1 THEN N'HostGap'
   WHEN @Usable<@Expected THEN N'CollectorFault' ELSE N'Complete' END,
 HostGapObserved=CASE WHEN @Gap=1 THEN 1 ELSE HostGapObserved END
WHERE SessionId=@SessionId AND EndedUtc IS NULL;
""";

    private const string BeginCycleSql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME();
""" + AssertLeaseSql + """

IF @Now<@Scheduled OR @Now>=@Deadline THROW 52206, 'Cycle is outside its operational collection window.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveHostLease WHERE LeaseId=@LeaseId AND ExpiresUtc>=@Deadline)
    THROW 52207, 'Host lease must cover the entire collection deadline.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.DelphiLiveSession WHERE SessionId=@SessionId
              AND SessionOpenUtc<=@Start AND SessionCloseUtc>=@End AND SessionState IN (N'Frozen',N'Monitoring'))
    THROW 52208, 'Cycle does not belong to an active frozen regular session.', 1;
DECLARE @EpochId UNIQUEIDENTIFIER;
SELECT @EpochId=ContinuityEpochId FROM dbo.DelphiLiveContinuityEpoch WITH (UPDLOCK,HOLDLOCK)
WHERE SessionId=@SessionId AND EpochNumber=@Epoch AND LeaseId=@LeaseId AND LeaseFencingToken=@Fence AND EndedUtc IS NULL;
IF @EpochId IS NULL THROW 52209, 'Recover the host continuity epoch before beginning collection.', 1;
DECLARE @Expected TABLE (Symbol NVARCHAR(20) PRIMARY KEY,PriorityClass NVARCHAR(32),PriorityOrdinal INT);
INSERT @Expected SELECT Symbol,PriorityClass,PriorityOrdinal FROM OPENJSON(@Targets)
WITH (Symbol NVARCHAR(20),PriorityClass NVARCHAR(32),PriorityOrdinal INT);
IF EXISTS (SELECT Symbol FROM dbo.DelphiLiveSessionSymbol WHERE SessionId=@SessionId
           AND RequiredFromBarEndUtc<=@End AND RequiredThroughBarEndUtc>=@End EXCEPT SELECT Symbol FROM @Expected)
 OR EXISTS (SELECT Symbol FROM @Expected EXCEPT SELECT Symbol FROM dbo.DelphiLiveSessionSymbol
            WHERE SessionId=@SessionId AND RequiredFromBarEndUtc<=@End AND RequiredThroughBarEndUtc>=@End)
    THROW 52210, 'Cycle must contain every durable expected symbol exactly once.', 1;
IF EXISTS (SELECT 1 FROM dbo.IntradayCollectionCycle WITH (UPDLOCK,HOLDLOCK) WHERE CycleId=@CycleId)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.IntradayCollectionCycle WHERE CycleId=@CycleId AND SessionId=@SessionId
      AND ContinuityEpochId=@EpochId AND LeaseId=@LeaseId AND LeaseFencingToken=@Fence
      AND BarStartUtc=@Start AND BarEndUtc=@End AND ScheduledStartUtc=@Scheduled AND DeadlineUtc=@Deadline)
      THROW 52211, 'Cycle identity cannot be reused with a different request.', 1;
    IF EXISTS (SELECT Symbol,PriorityClass,PriorityOrdinal FROM @Expected EXCEPT
               SELECT Symbol,PriorityClass,PriorityOrdinal FROM dbo.IntradayCollectionSlot WHERE CycleId=@CycleId)
      THROW 52212, 'Retried cycle cannot change its frozen expected targets or ordering.', 1;
    RETURN;
END;
-- Deadline settlement releases the one-active-cycle constraint; it never opens
-- a second provider request while a host owns unfinished in-window work.
UPDATE sl SET Disposition=N'CycleDeadlineExceeded',DispositionCode=N'CycleDeadlineExceeded',
    OperationallyUsable=0,MissedOperationalDeadline=1,SettledUtc=@Now,UpdatedUtc=@Now
FROM dbo.IntradayCollectionSlot sl JOIN dbo.IntradayCollectionCycle c ON c.CycleId=sl.CycleId
WHERE c.CycleStatus IN (N'Planned',N'Collecting') AND c.DeadlineUtc<=@Now AND sl.Disposition=N'Pending';
UPDATE dbo.IntradayCollectionCycle SET CycleStatus=N'DeadlineExceeded',CompletedUtc=@Now,
    CompletionCode=N'CycleDeadlineExceeded',SettledSlotCount=ExpectedSlotCount,UpdatedUtc=@Now
WHERE CycleStatus IN (N'Planned',N'Collecting') AND DeadlineUtc<=@Now;
IF EXISTS (SELECT 1 FROM dbo.IntradayCollectionCycle WITH (UPDLOCK,HOLDLOCK) WHERE CycleStatus=N'Collecting')
    THROW 52213, 'Another Delphi Live collection cycle is still running.', 1;
INSERT dbo.IntradayCollectionCycle
  (CycleId,SessionId,ContinuityEpochId,LeaseId,LeaseFencingToken,CollectionPurpose,Provider,CollectorVersion,
   SourceContractVersion,IntervalMinutes,BarStartUtc,BarEndUtc,ScheduledStartUtc,DeadlineUtc,CycleStatus,
   ExpectedSlotCount,SettledSlotCount,StartedUtc)
SELECT @CycleId,@SessionId,@EpochId,@LeaseId,@Fence,N'DelphiLiveShared',@Provider,N'IntradayEvidenceCollectorV3',
   1,5,@Start,@End,@Scheduled,@Deadline,N'Collecting',COUNT(*),0,@Now FROM @Expected;
INSERT dbo.IntradayCollectionSlot
  (CollectionSlotId,CycleId,SessionId,SessionSymbolId,Symbol,IntervalMinutes,ExpectedBarStartUtc,ExpectedBarEndUtc,
   ScheduledStartUtc,DeadlineUtc,IsXiuBenchmark,PriorityClass,PriorityOrdinal,RequiredByJson,RequestAttemptCount,
   Disposition,OperationallyUsable,MissedOperationalDeadline)
SELECT NEWID(),@CycleId,@SessionId,ss.SessionSymbolId,ss.Symbol,5,@Start,@End,@Scheduled,@Deadline,
   ss.IsXiuBenchmark,e.PriorityClass,e.PriorityOrdinal,ss.SourceIdentityJson,0,N'Pending',0,0
FROM @Expected e JOIN dbo.DelphiLiveSessionSymbol ss ON ss.SessionId=@SessionId AND ss.Symbol=e.Symbol;
UPDATE dbo.DelphiLiveSession SET SessionState=N'Monitoring',UpdatedUtc=@Now WHERE SessionId=@SessionId;
""";
}
