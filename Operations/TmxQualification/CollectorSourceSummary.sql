-- October 28 read-only qualification evidence from the sole installed collector.
-- Submit through the installed SQL helper; retain receipts privately and verify query SHA256.
-- No prices/account identifiers. Backfill coverage and timely first arrival remain separate.
DECLARE @open datetime2 = '2026-10-28T13:30:00';
DECLARE @close datetime2 = '2026-10-28T20:00:00';
DECLARE @now datetime2 = SYSUTCDATETIME();
WITH attempts AS (
 SELECT Symbol,COUNT_BIG(*) Attempts,MIN(ReservedUtc) FirstRequestUtc,MAX(ReservedUtc) LastRequestUtc,
 MAX(ExpectedEndUtc) LastAttemptedEndUtc
 FROM dbo.MarketCollectorAttempt
 WHERE SourceContract=N'TmxMinuteCollectorV1' AND SessionOpenUtc=@open
 GROUP BY Symbol
), receipts AS (
 SELECT Symbol,COUNT_BIG(*) Receipts,
 SUM(CASE WHEN JSON_VALUE(ContentJson,'$.FailureCode') IS NOT NULL THEN 1 ELSE 0 END) FailedReceipts,
 MAX(TRY_CONVERT(datetime2,JSON_VALUE(ContentJson,'$.ReceivedUtc'))) LastReceiptUtc
 FROM dbo.MarketCollectorRequest
 WHERE SourceContract=N'TmxMinuteCollectorV1' AND SessionOpenUtc=@open
 GROUP BY Symbol
), observations AS (
 SELECT Symbol,COUNT_BIG(*) CompletedBars,
 SUM(CASE WHEN JSON_VALUE(ContentJson,'$.ObservedOnTime')=N'true' THEN 1 ELSE 0 END) TimelyFirstArrivals,
 MAX(DATEADD(MINUTE,1,BarStartUtc)) LatestCompletedEndUtc,
 MAX(DATEDIFF(MILLISECOND,DATEADD(MINUTE,1,BarStartUtc),TRY_CONVERT(datetime2,JSON_VALUE(ContentJson,'$.CheckedUtc')))) MaximumFirstArrivalMilliseconds
 FROM dbo.MarketCollectorObservation
 WHERE SourceContract=N'TmxMinuteCollectorV1' AND SessionOpenUtc=@open
 GROUP BY Symbol
), conflicts AS (
 SELECT Symbol,COUNT_BIG(*) ConflictingBars FROM dbo.MarketCollectorConflict
 WHERE SourceContract=N'TmxMinuteCollectorV1' AND SessionOpenUtc=@open GROUP BY Symbol
)
SELECT TOP(64) a.Symbol,a.Attempts,a.FirstRequestUtc,a.LastRequestUtc,a.LastAttemptedEndUtc,
 COALESCE(r.Receipts,0) Receipts,COALESCE(r.FailedReceipts,0) FailedReceipts,r.LastReceiptUtc,
 COALESCE(o.CompletedBars,0) CompletedBars,COALESCE(o.TimelyFirstArrivals,0) TimelyFirstArrivals,
 COALESCE(o.CompletedBars,0)-COALESCE(o.TimelyFirstArrivals,0) LateFirstArrivals,
 o.LatestCompletedEndUtc,o.MaximumFirstArrivalMilliseconds,COALESCE(c.ConflictingBars,0) ConflictingBars,
 CASE WHEN @now<@open THEN 0 ELSE DATEDIFF(MINUTE,@open,CASE WHEN @now>@close THEN @close ELSE @now END) END ElapsedSessionMinutes
FROM attempts a LEFT JOIN receipts r ON r.Symbol=a.Symbol
 LEFT JOIN observations o ON o.Symbol=a.Symbol LEFT JOIN conflicts c ON c.Symbol=a.Symbol
ORDER BY a.Symbol;
SELECT SourceContract,ExpiresUtc,
 CAST(CASE WHEN ExpiresUtc>@now THEN 1 ELSE 0 END AS bit) WriterLeaseCurrent
FROM dbo.MarketCollectorLease WHERE SourceContract=N'TmxMinuteCollectorV1';
