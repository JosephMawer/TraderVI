-- Bounded recovery supervision. No account values, symbols, authorization contents or archive payloads.
SET NOCOUNT ON;
SELECT SYSUTCDATETIME() CheckedUtc;
SELECT RunId,Status,BoundaryUtc,UpdatedUtc,Revision,PlanHash,
 TRY_CONVERT(bigint,JSON_VALUE(ProgressJson,'$.VerifiedUnits')) VerifiedUnits,
 TRY_CONVERT(int,JSON_VALUE(ProgressJson,'$.TargetIndex')) TargetIndex,
 TRY_CONVERT(bigint,JSON_VALUE(ProgressJson,'$.NextRevision')) NextRevision,
 (SELECT SUM(CASE WHEN Family=4 THEN ThroughRevision
                 WHEN ThroughRevision>=FromRevision THEN ThroughRevision-FromRevision+1 ELSE 0 END)
  FROM OPENJSON(PlanJson,'$.Targets') WITH(Family int,FromRevision bigint,ThroughRevision bigint)) PlannedUnitUpperBound
FROM dbo.EngineAuditRun WHERE Kind=N'Recovery' ORDER BY BoundaryUtc DESC;
SELECT FailureCode,ProtectionUntrusted,COUNT_BIG(*) UnresolvedIncidents
FROM dbo.EngineIntegrityIncident i WHERE NOT EXISTS
 (SELECT 1 FROM dbo.EngineIntegrityResolution r WHERE r.IncidentId=i.IncidentId)
GROUP BY FailureCode,ProtectionUntrusted;
SELECT Kind,COUNT_BIG(*) Checkpoints,
 SUM(CASE WHEN StateHash=CONVERT(char(64),HASHBYTES('SHA2_256',
  CONVERT(varchar(max),StateJson COLLATE Latin1_General_100_BIN2_UTF8)),2) THEN 0 ELSE 1 END) BadHashes
FROM dbo.EngineCurrentState GROUP BY Kind;
