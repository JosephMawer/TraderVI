#nullable enable

namespace Core.Db;

public sealed partial class DelphiLiveCollectionRepository
{
    private const string RecordReceiptSql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME(),@Slot UNIQUEIDENTIFIER,@Session UNIQUEIDENTIFIER,
    @Lease UNIQUEIDENTIFIER,@Fence BIGINT,@CurrentDisposition NVARCHAR(32),@CycleStatus NVARCHAR(32),
    @Code NVARCHAR(128),@Tree NVARCHAR(16),@LeaseValid BIT=0,@Poll UNIQUEIDENTIFIER,
    @Evidence UNIQUEIDENTIFIER,@Conflict BIT=0,@Duplicate BIT=0,@InsertBar BIT=0,
    @Reason NVARCHAR(64)=@Disposition,@StoredRequest DATETIME2,@StoredReceived DATETIME2,
    @CanonicalPoll UNIQUEIDENTIFIER,@ExistingPoll BIT=0;
SELECT @Slot=s.CollectionSlotId,@Session=s.SessionId,@Lease=c.LeaseId,@Fence=c.LeaseFencingToken,
    @CurrentDisposition=s.Disposition,@CycleStatus=c.CycleStatus,@Code=l.CodeCommit,@Tree=l.WorkingTreeState,
    @LeaseValid=CASE WHEN l.IsHeld=1 AND l.ExpiresUtc>@Now THEN 1 ELSE 0 END,
    @StoredRequest=s.RequestStartedUtc,@StoredReceived=s.ReceivedUtc
FROM dbo.IntradayCollectionSlot s WITH (UPDLOCK,HOLDLOCK)
JOIN dbo.IntradayCollectionCycle c WITH (UPDLOCK,HOLDLOCK) ON c.CycleId=s.CycleId
JOIN dbo.DelphiLiveHostLease l WITH (UPDLOCK,HOLDLOCK) ON l.LeaseId=c.LeaseId AND l.FencingToken=c.LeaseFencingToken
WHERE s.CycleId=@CycleId AND s.Symbol=@Symbol AND s.IntervalMinutes=5
  AND s.ExpectedBarStartUtc=@Start AND s.ExpectedBarEndUtc=@End AND s.DeadlineUtc=@Deadline
  AND s.PriorityOrdinal=@Ordinal AND @Request>=c.StartedUtc;
IF @Slot IS NULL THROW 52214, 'Receipt does not identify its exact durable cycle slot.', 1;
IF NOT EXISTS (SELECT 1 FROM OPENJSON(@OwnedLeases) WHERE TRY_CONVERT(UNIQUEIDENTIFIER,value)=@Lease)
    THROW 52215, 'Receipt belongs to another host lease.', 1;
IF @Received>@Now THROW 52216, 'Receipt timestamp is later than the durable store clock.', 1;
IF @StoredRequest IS NOT NULL AND @StoredRequest<>@Request
    THROW 52217, 'One slot cannot start a second primary provider request.', 1;

-- An ambiguous-commit retry returns its original persisted result without
-- incrementing attempts or replacing operational evidence.
IF EXISTS (SELECT 1 FROM dbo.IntradayCollectionReceipt WHERE CollectionSlotId=@Slot AND ReceiptSha256=@ReceiptHash)
BEGIN
    SELECT Disposition,PollObservationId,EvidenceBarId FROM dbo.IntradayCollectionReceipt
    WHERE CollectionSlotId=@Slot AND ReceiptSha256=@ReceiptHash;
    RETURN;
END;

SELECT @Poll=ObservationId FROM dbo.IntradayPollObservation WITH (UPDLOCK,HOLDLOCK)
WHERE PollCycleId=@CycleId AND Symbol=@Symbol AND IntervalMinutes=5;
IF @Poll IS NOT NULL SET @ExistingPoll=1;
IF @SuppliedPoll IS NOT NULL AND (@Poll IS NULL OR @Poll<>@SuppliedPoll)
    THROW 52218, 'Supplied observation identity does not match the canonical natural key.', 1;
IF @HasBar=1
BEGIN
    SELECT @Evidence=b.EvidenceBarId,
      @Conflict=CASE WHEN b.[Open]<>@Open OR b.High<>@High OR b.Low<>@Low OR b.[Close]<>@Close OR b.Volume<>@Volume
          OR o.Provider<>@Provider OR o.SourceContractVersion<>@SourceContract OR o.EvidenceSchemaVersion<>1
          OR o.AuditState NOT IN (N'Valid',N'Degraded') THEN 1 ELSE 0 END
    FROM dbo.IntradayEvidenceBar b WITH (UPDLOCK,HOLDLOCK)
    JOIN dbo.IntradayPollObservation o ON o.ObservationId=b.FirstObservationId
      AND o.Symbol=b.Symbol AND o.IntervalMinutes=b.IntervalMinutes
    WHERE b.Symbol=@Symbol AND b.IntervalMinutes=5 AND b.EventUtc=@Start;
    IF @SuppliedBar IS NOT NULL AND (@Evidence IS NULL OR @Evidence<>@SuppliedBar)
        THROW 52219, 'Supplied evidence identity does not match the canonical natural key.', 1;
    IF @Evidence IS NULL
    BEGIN
        SET @Evidence=NEWID(); SET @InsertBar=1;
    END
    ELSE IF @Conflict=0 SET @Duplicate=1;
END
ELSE IF @SuppliedBar IS NOT NULL
    THROW 52220, 'An invalid or absent exact bar cannot reference canonical evidence.', 1;

IF @Conflict=1
BEGIN
    SET @Disposition=N'ConflictingDuplicate'; SET @Reason=N'CanonicalEvidenceConflict';
END
ELSE IF @HasBar=1 AND (@Received>=@Deadline OR @Now>=@Deadline OR @LeaseValid=0
                      OR @CycleStatus<>N'Collecting'
                      OR @CurrentDisposition NOT IN (N'Pending',N'OperationalOnTime',N'IdenticalDuplicate'))
BEGIN
    SET @Disposition=N'LateResearchOnly';
    SET @Reason=CASE WHEN @LeaseValid=0 THEN N'LeaseLost' WHEN @Received>=@Deadline THEN N'LateReceipt'
         WHEN @Now>=@Deadline THEN N'LatePersistence' ELSE N'CycleAlreadySettled' END;
END
ELSE IF @HasBar=1 AND @Duplicate=1
BEGIN
    SET @Disposition=N'IdenticalDuplicate'; SET @Reason=N'CanonicalEvidenceReused';
END;

IF @Poll IS NULL
BEGIN
    SET @Poll=NEWID();
    INSERT dbo.IntradayPollObservation
      (ObservationId,PollCycleId,Purpose,Symbol,IntervalMinutes,EvidenceSchemaVersion,Provider,SourceContractVersion,
       CollectorVersion,PolicyVersion,CodeCommit,WorkingTreeState,RequestedStartUtc,RequestedEndUtc,FetchStartedUtc,
       ReceivedUtc,AttemptCount,RequestCount,ReturnedBarCount,CompletedBarCount,PersistedNewBarCount,
       LatestReturnedEventUtc,LatestCompletedEventUtc,AuditState,AuditCode)
    VALUES (@Poll,@CycleId,N'PaperMonitor',@Symbol,5,1,@Provider,@SourceContract,N'IntradayEvidenceCollectorV3',
      NULL,@Code,@Tree,@Start,@End,COALESCE(@ProviderFetch,@Request),@Received,
      COALESCE(@ProviderAttempts,0),COALESCE(@ProviderRequests,0),CONVERT(INT,@HasBar),CONVERT(INT,@HasBar),
      CONVERT(INT,@InsertBar),CASE WHEN @HasBar=1 THEN @Start ELSE NULL END,
      CASE WHEN @HasBar=1 THEN @Start ELSE NULL END,
      CASE WHEN @Conflict=1 THEN N'Invalid' WHEN @HasBar=1 THEN N'Valid' ELSE N'Failed' END,@Reason);
END;
SET @CanonicalPoll=@Poll;
-- A late response following a recorded timeout must not borrow the earlier
-- timeout timestamp as its first-observation time. The immutable legacy poll
-- natural key allows one primary poll per slot, so the later source receipt has
-- a distinct audit poll identity linked back to this same collection slot by
-- IntradayCollectionReceipt. It is not a second provider request or live cycle.
IF @InsertBar=1 AND @ExistingPoll=1
BEGIN
    SET @CanonicalPoll=NEWID();
    INSERT dbo.IntradayPollObservation
      (ObservationId,PollCycleId,Purpose,Symbol,IntervalMinutes,EvidenceSchemaVersion,Provider,SourceContractVersion,
       CollectorVersion,PolicyVersion,CodeCommit,WorkingTreeState,RequestedStartUtc,RequestedEndUtc,FetchStartedUtc,
       ReceivedUtc,AttemptCount,RequestCount,ReturnedBarCount,CompletedBarCount,PersistedNewBarCount,
       LatestReturnedEventUtc,LatestCompletedEventUtc,AuditState,AuditCode)
    VALUES (@CanonicalPoll,NEWID(),N'PaperMonitor',@Symbol,5,1,@Provider,@SourceContract,N'IntradayEvidenceCollectorV3',
      NULL,@Code,@Tree,@Start,@End,COALESCE(@ProviderFetch,@Request),@Received,
      COALESCE(@ProviderAttempts,0),COALESCE(@ProviderRequests,0),1,1,1,@Start,@Start,N'Valid',N'LateReceiptAfterPrimaryMiss');
END;
IF @InsertBar=1
    INSERT dbo.IntradayEvidenceBar
      (EvidenceBarId,FirstObservationId,Symbol,IntervalMinutes,EventUtc,[Open],High,Low,[Close],Volume)
    VALUES (@Evidence,@CanonicalPoll,@Symbol,5,@Start,@Open,@High,@Low,@Close,@Volume);
IF @Conflict=1 AND NOT EXISTS (SELECT 1 FROM dbo.IntradayEvidenceConflict
                              WHERE CollectionSlotId=@Slot AND IncomingPayloadSha256=@Hash)
    INSERT dbo.IntradayEvidenceConflict
      (EvidenceConflictId,CollectionSlotId,CycleId,SessionId,PollObservationId,ExistingEvidenceBarId,Symbol,
       IntervalMinutes,ExistingBarEventUtc,IncomingEventUtc,IncomingOpen,IncomingHigh,IncomingLow,IncomingClose,
       IncomingVolume,IncomingPayloadSha256,ReceivedUtc,ConflictCode,ResolutionDisposition)
    VALUES (NEWID(),@Slot,@CycleId,@Session,@Poll,@Evidence,@Symbol,5,@Start,@Start,@Open,@High,@Low,@Close,
      @Volume,@Hash,@Received,N'CanonicalEvidenceConflict',N'Unresolved');

-- Use database settlement time after canonical persistence, never the host's
-- request/receipt clock as evidence that persistence met the deadline.
SET @Now=SYSUTCDATETIME();
IF @Disposition IN (N'OperationalOnTime',N'IdenticalDuplicate') AND @Now>=@Deadline
BEGIN
    SET @Disposition=N'LateResearchOnly'; SET @Reason=N'LatePersistence';
END;
DECLARE @Operational BIT=CASE WHEN @Disposition IN (N'OperationalOnTime',N'IdenticalDuplicate') THEN 1 ELSE 0 END;
-- A duplicate never replaces a previously settled operational slot. Its new
-- source diagnostics remain in the append-only receipt and conflict journals.
IF @CurrentDisposition=N'Pending'
 OR (@CurrentDisposition NOT IN (N'OperationalOnTime',N'IdenticalDuplicate') AND @HasBar=1 AND @StoredReceived IS NULL)
    UPDATE dbo.IntradayCollectionSlot SET RequestAttemptCount=COALESCE(@ProviderAttempts,1),RequestStartedUtc=@Request,
      ReceivedUtc=@Received,PollCycleId=@CycleId,PollObservationId=@Poll,EvidenceBarId=@Evidence,
      EvidenceBarEventUtc=CASE WHEN @Evidence IS NULL THEN NULL ELSE @Start END,
      Disposition=CASE WHEN @Operational=1 THEN N'Pending' ELSE @Disposition END,
      DispositionCode=CASE WHEN @Operational=1 THEN N'AwaitingDurabilityVerification' ELSE @Reason END,
      OperationallyUsable=0,MissedOperationalDeadline=CASE WHEN @Operational=1 THEN 0 ELSE 1 END,
      SettledUtc=CASE WHEN @Operational=1 THEN NULL ELSE @Now END,UpdatedUtc=@Now
    WHERE CollectionSlotId=@Slot;
INSERT dbo.IntradayCollectionReceipt
  (ReceiptId,CollectionSlotId,CycleId,SessionId,Symbol,IntervalMinutes,RequestStartedUtc,ReceivedUtc,SettledUtc,
   Disposition,DispositionCode,OperationallyUsable,PollObservationId,EvidenceBarId,NormalizedResponseJson,ReceiptSha256,
   ProviderAttemptCount,ProviderRequestCount,ProviderFetchStartedUtc)
VALUES (NEWID(),@Slot,@CycleId,@Session,@Symbol,5,@Request,@Received,@Now,@Disposition,@Reason,@Operational,
    @CanonicalPoll,@Evidence,@ReceiptJson,@ReceiptHash,@ProviderAttempts,@ProviderRequests,@ProviderFetch);
UPDATE dbo.IntradayCollectionCycle SET SettledSlotCount=
    (SELECT COUNT(*) FROM dbo.IntradayCollectionSlot WHERE CycleId=@CycleId AND Disposition<>N'Pending'),UpdatedUtc=@Now
WHERE CycleId=@CycleId;
SELECT @Disposition,@Poll,@Evidence;
""";

    private const string VerifyDurabilitySql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME(),@Slot UNIQUEIDENTIFIER,@Lease UNIQUEIDENTIFIER,@Deadline DATETIME2,
    @LeaseValid BIT=0,@State NVARCHAR(32),@CycleState NVARCHAR(32),@Usable BIT;
SELECT @Slot=s.CollectionSlotId,@Lease=c.LeaseId,@Deadline=s.DeadlineUtc,@State=s.Disposition,
    @CycleState=c.CycleStatus,@Usable=s.OperationallyUsable,
    @LeaseValid=CASE WHEN l.IsHeld=1 AND l.ExpiresUtc>@Now THEN 1 ELSE 0 END
FROM dbo.IntradayCollectionSlot s WITH(UPDLOCK,HOLDLOCK)
JOIN dbo.IntradayCollectionCycle c ON c.CycleId=s.CycleId
JOIN dbo.DelphiLiveHostLease l ON l.LeaseId=c.LeaseId AND l.FencingToken=c.LeaseFencingToken
WHERE s.CycleId=@CycleId AND s.Symbol=@Symbol;
IF @Slot IS NULL OR NOT EXISTS(SELECT 1 FROM OPENJSON(@OwnedLeases) WHERE TRY_CONVERT(UNIQUEIDENTIFIER,value)=@Lease)
    THROW 52224,'Durability verification does not own the collection slot.',1;
IF EXISTS(SELECT 1 FROM dbo.IntradayEvidenceConflict WHERE CollectionSlotId=@Slot)
BEGIN
    SELECT N'ConflictingDuplicate';
    RETURN;
END;
IF @Usable=1
BEGIN
    SELECT @Disposition;
    RETURN;
END;
IF @State=N'Pending'
BEGIN
    DECLARE @OnTime BIT=CASE WHEN @Now<@Deadline AND @LeaseValid=1 AND @CycleState=N'Collecting' THEN 1 ELSE 0 END;
    UPDATE dbo.IntradayCollectionSlot SET
      Disposition=CASE WHEN @OnTime=1 THEN @Disposition ELSE N'LateResearchOnly' END,
      DispositionCode=CASE WHEN @OnTime=1 THEN N'DurableOnTime' WHEN @LeaseValid=0 THEN N'LeaseLost'
        WHEN @Now>=@Deadline THEN N'LatePersistence' ELSE N'CycleAlreadySettled' END,
      OperationallyUsable=@OnTime,MissedOperationalDeadline=CASE WHEN @OnTime=1 THEN 0 ELSE 1 END,
      SettledUtc=@Now,UpdatedUtc=@Now
    WHERE CollectionSlotId=@Slot AND PollObservationId IS NOT NULL AND EvidenceBarId IS NOT NULL;
    IF @@ROWCOUNT<>1 THROW 52225,'Canonical evidence is absent during durability verification.',1;
    IF @OnTime=0 SET @Disposition=N'LateResearchOnly';
END
ELSE SET @Disposition=N'LateResearchOnly';
UPDATE dbo.IntradayCollectionCycle SET SettledSlotCount=
    (SELECT COUNT(*) FROM dbo.IntradayCollectionSlot WHERE CycleId=@CycleId AND Disposition<>N'Pending'),UpdatedUtc=@Now
WHERE CycleId=@CycleId;
SELECT @Disposition;
""";

    private const string CompleteCycleSql = """
DECLARE @Now DATETIME2=SYSUTCDATETIME(),@Lease UNIQUEIDENTIFIER,@Deadline DATETIME2,@State NVARCHAR(32),
    @LeaseValid BIT=0,@Session UNIQUEIDENTIFIER;
SELECT @Lease=c.LeaseId,@Deadline=c.DeadlineUtc,@State=c.CycleStatus,@Session=c.SessionId,
    @LeaseValid=CASE WHEN l.IsHeld=1 AND l.ExpiresUtc>@Now THEN 1 ELSE 0 END
FROM dbo.IntradayCollectionCycle c WITH (UPDLOCK,HOLDLOCK)
JOIN dbo.DelphiLiveHostLease l WITH (UPDLOCK,HOLDLOCK) ON l.LeaseId=c.LeaseId AND l.FencingToken=c.LeaseFencingToken
WHERE c.CycleId=@CycleId;
IF @Lease IS NULL THROW 52221, 'Unknown Delphi Live cycle.', 1;
IF NOT EXISTS (SELECT 1 FROM OPENJSON(@OwnedLeases) WHERE TRY_CONVERT(UNIQUEIDENTIFIER,value)=@Lease)
    THROW 52222, 'Cycle completion belongs to another host lease.', 1;
IF @State NOT IN (N'Planned',N'Collecting') RETURN;
IF @LeaseValid=0 AND @Status=N'Completed' SET @Status=N'Failed';
IF @Now>=@Deadline SET @Status=N'DeadlineExceeded';
IF @Status=N'DeadlineExceeded' AND @Now<@Deadline SET @Status=N'Cancelled';
UPDATE dbo.IntradayCollectionSlot SET
    Disposition=CASE WHEN @Now>=@Deadline THEN N'CycleDeadlineExceeded' ELSE N'CollectionFailed' END,
    DispositionCode=CASE WHEN @LeaseValid=0 THEN N'LeaseLost' WHEN @Now>=@Deadline THEN N'CycleDeadlineExceeded'
      ELSE N'UnfinishedCollection' END,OperationallyUsable=0,MissedOperationalDeadline=1,SettledUtc=@Now,UpdatedUtc=@Now
WHERE CycleId=@CycleId AND Disposition=N'Pending';
UPDATE dbo.IntradayCollectionCycle SET CycleStatus=
    CASE WHEN @Status=N'Failed' THEN N'Cancelled' ELSE @Status END,
    CompletedUtc=@Now,CompletionCode=@Status,SettledSlotCount=ExpectedSlotCount,UpdatedUtc=@Now
WHERE CycleId=@CycleId;
IF EXISTS (SELECT 1 FROM dbo.IntradayCollectionSlot WHERE CycleId=@CycleId AND OperationallyUsable=0)
    UPDATE dbo.DelphiLiveSession SET CoverageState=N'Blocked',UpdatedUtc=@Now WHERE SessionId=@Session;
""";
}
