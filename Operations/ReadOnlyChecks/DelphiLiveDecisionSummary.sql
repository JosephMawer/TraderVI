SET NOCOUNT ON;
-- Shared revisions require the read-only EngineOperation.ExecutionDiagnostics RPC with the exact binding ID.
-- BrokerManagedSessionRepository.ReadDiagnosticsAsync verifies the original manifest, all referenced bytes
-- and the saved row identity. This legacy SQL helper does not decode shared archive references.
DECLARE @Snapshot nvarchar(max);
SELECT TOP(1) @Snapshot=SnapshotJson FROM dbo.BrokerManagedSessionRevision ORDER BY SavedAt DESC;
SELECT SYSUTCDATETIME() AS CheckedUtc,
 CASE WHEN JSON_VALUE(@Snapshot,'$."$engineArchive".version') IS NULL THEN N'Inline'
      ELSE N'Shared archive: use the qualified engine reader; raw JSON paths below are incomplete' END AS StorageFormat,
 JSON_VALUE(@Snapshot,'$.SavedAt') AS SessionSavedAt,
 JSON_VALUE(@Snapshot,'$.Event') AS SessionEvent,
 CASE WHEN JSON_VALUE(@Snapshot,'$."$engineArchive".version') IS NOT NULL THEN NULL
      WHEN JSON_QUERY(@Snapshot,'$.LatestDecision') IS NULL THEN 0 ELSE 1 END AS HasDecisionTrace,
 JSON_VALUE(@Snapshot,'$.LatestDecision.EvaluatedUtc') AS TraceEvaluationUtc,
 JSON_VALUE(@Snapshot,'$.LatestDecision.RecordedAt') AS TraceRecordedAt,
 JSON_VALUE(@Snapshot,'$.LatestDecision.SelectionOutcome') AS SelectionOutcome,
 JSON_VALUE(@Snapshot,'$.LatestDecision.FailureType') AS FailureType,
 JSON_VALUE(@Snapshot,'$.LatestDecision.FailureSqlNumber') AS FailureSqlNumber,
 JSON_VALUE(@Snapshot,'$.LatestDecision.FailedOrderCheck') AS FailedOrderCheck,
 JSON_VALUE(@Snapshot,'$.LatestDecision.LocalCheckExplanation') AS LocalCheckExplanation;
SELECT JSON_VALUE(value,'$.Outcome') AS EntryOutcome,
 JSON_VALUE(value,'$.ExecutionSelection') AS ExecutionSelection,COUNT(*) AS SignalCount,
 MIN(TRY_CONVERT(datetime2,JSON_VALUE(value,'$.SignalBarUtc'))) AS EarliestSignalBarUtc,
 MAX(TRY_CONVERT(datetime2,JSON_VALUE(value,'$.SignalBarUtc'))) AS LatestSignalBarUtc
FROM OPENJSON(@Snapshot,'$.LatestDecision.Entries')
GROUP BY JSON_VALUE(value,'$.Outcome'),JSON_VALUE(value,'$.ExecutionSelection');
SELECT JSON_VALUE(value,'$.Outcome') AS QuoteOutcome,
 JSON_VALUE(value,'$.ErrorType') AS ErrorType,COUNT(*) AS AttemptCount,
 MIN(TRY_CONVERT(datetime2,JSON_VALUE(value,'$.RequestedUtc'))) AS EarliestRequestUtc,
 MAX(TRY_CONVERT(datetime2,JSON_VALUE(value,'$.ReceivedUtc'))) AS LatestReceiptUtc
FROM OPENJSON(@Snapshot,'$.LatestDecision.QuoteAttempts')
GROUP BY JSON_VALUE(value,'$.Outcome'),JSON_VALUE(value,'$.ErrorType');
SELECT CONVERT(nvarchar(256),value) AS NewRiskBlock FROM OPENJSON(@Snapshot,'$.LatestDecision.NewRiskBlocks');
SELECT CONVERT(nvarchar(256),value) AS QuoteReadBlock FROM OPENJSON(@Snapshot,'$.LatestDecision.QuoteReadBlocks');
