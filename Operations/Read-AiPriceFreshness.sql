-- SELECT-only diagnostic. No collection, account writes, schema changes or trading permission.
-- Ages use completed bar END, not receipt time; re-reading a bar cannot make its price fresh.
-- Uses today's saved reviewed-session bounds. Missing session produces no invented market window.
-- Output contains aggregates and times only, never symbols, account IDs or prices.
SET NOCOUNT ON;
SET LOCK_TIMEOUT 10000;
DECLARE @NowUtc datetime2 = SYSUTCDATETIME();
DECLARE @Date date = CONVERT(date, @NowUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Eastern Standard Time');
DECLARE @OpenUtc datetime2, @CloseUtc datetime2;
SELECT @OpenUtc=SessionOpenUtc, @CloseUtc=SessionCloseUtc
FROM dbo.DelphiLiveSession WHERE TradingDate=@Date;
IF @OpenUtc IS NULL THROW 50000, 'No saved reviewed session for today; freshness was not assessed.', 1;
DECLARE @EndUtc datetime2 = CASE WHEN @NowUtc < @CloseUtc THEN @NowUtc ELSE @CloseUtc END;
DECLARE @StartUtc datetime2 = CASE WHEN DATEADD(hour,-1,@EndUtc) > @OpenUtc
    THEN DATEADD(hour,-1,@EndUtc) ELSE @OpenUtc END;

SELECT @NowUtc CheckedUtc, @Date TradingDate, @StartUtc WindowStartUtc, @EndUtc WindowEndUtc,
 JSON_QUERY((SELECT CoverageState,HostGapObserved FROM dbo.DelphiLiveSession
   WHERE TradingDate=@Date FOR JSON PATH)) SessionCoverage,
 JSON_QUERY((SELECT CollectorVersion,IntervalMinutes,AuditState,AuditCode,COUNT(*) Receipts,
   COUNT(DISTINCT Symbol) Symbols,MIN(ReceivedUtc) FirstReceivedUtc,MAX(ReceivedUtc) LastReceivedUtc,
   MIN(DATEDIFF_BIG(millisecond,DATEADD(minute,IntervalMinutes,LatestCompletedEventUtc),ReceivedUtc)/1000.0) MinCompletedPriceAgeSeconds,
   MAX(DATEDIFF_BIG(millisecond,DATEADD(minute,IntervalMinutes,LatestCompletedEventUtc),ReceivedUtc)/1000.0) MaxCompletedPriceAgeSeconds,
   SUM(CASE WHEN LatestCompletedEventUtc IS NULL THEN 1 ELSE 0 END) WithoutCompletedPrice
   FROM dbo.IntradayPollObservation
   WHERE FetchStartedUtc>=@StartUtc AND FetchStartedUtc<=@EndUtc AND Purpose=N'PaperMonitor'
   GROUP BY CollectorVersion,IntervalMinutes,AuditState,AuditCode FOR JSON PATH)) PollReceipts,
 JSON_QUERY((SELECT b.IntervalMinutes,COUNT(*) NewCanonicalBars,
   MIN(DATEDIFF_BIG(millisecond,DATEADD(minute,b.IntervalMinutes,b.EventUtc),o.ReceivedUtc)/1000.0) MinFirstReceiptDelaySeconds,
   MAX(DATEDIFF_BIG(millisecond,DATEADD(minute,b.IntervalMinutes,b.EventUtc),o.ReceivedUtc)/1000.0) MaxFirstReceiptDelaySeconds
   FROM dbo.IntradayEvidenceBar b JOIN dbo.IntradayPollObservation o ON o.ObservationId=b.FirstObservationId
   WHERE o.ReceivedUtc>=@StartUtc AND o.ReceivedUtc<=@EndUtc AND o.Purpose=N'PaperMonitor'
     AND o.AuditState IN(N'Valid',N'Degraded')
     AND b.EventUtc>=@StartUtc AND DATEADD(minute,b.IntervalMinutes,b.EventUtc)<=@EndUtc
   GROUP BY b.IntervalMinutes FOR JSON PATH)) FirstBarReceipts,
 JSON_QUERY((SELECT s.Disposition,s.DispositionCode,s.OperationallyUsable,COUNT(*) Slots,
   COUNT(DISTINCT s.CycleId) Cycles,
   MIN(DATEDIFF_BIG(millisecond,s.ExpectedBarEndUtc,s.ReceivedUtc)/1000.0) MinReceiptDelaySeconds,
   MAX(DATEDIFF_BIG(millisecond,s.ExpectedBarEndUtc,s.ReceivedUtc)/1000.0) MaxReceiptDelaySeconds
   FROM dbo.IntradayCollectionSlot s JOIN dbo.DelphiLiveSession d ON d.SessionId=s.SessionId
   WHERE d.TradingDate=@Date AND s.ExpectedBarEndUtc>=@StartUtc AND s.DeadlineUtc<=@NowUtc
   GROUP BY s.Disposition,s.DispositionCode,s.OperationallyUsable FOR JSON PATH)) MaturedLiveSlots,
 JSON_QUERY((SELECT CycleStatus,CompletionCode,COUNT(*) Cycles,
   MIN(BarEndUtc) FirstBarEndUtc,MAX(BarEndUtc) LastBarEndUtc,
   MIN(DATEDIFF_BIG(millisecond,BarEndUtc,CompletedUtc)/1000.0) MinCompletionDelaySeconds,
   MAX(DATEDIFF_BIG(millisecond,BarEndUtc,CompletedUtc)/1000.0) MaxCompletionDelaySeconds
   FROM dbo.IntradayCollectionCycle WHERE BarEndUtc>=@StartUtc AND BarEndUtc<=@EndUtc
     AND DeadlineUtc<=@NowUtc
   GROUP BY CycleStatus,CompletionCode FOR JSON PATH)) MaturedLiveCycles
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
