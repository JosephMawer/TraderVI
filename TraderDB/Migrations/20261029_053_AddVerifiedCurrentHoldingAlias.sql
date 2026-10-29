-- ADR-0088: one explicitly reviewed missing Broker alias, not a financial-record repair.
-- Manual SQLCMD inputs: ExpectedRevision and ExpectedHeadHash from the reviewed read-only receipt.
-- Preserve all positions, orders, risk references, permissions and historical observations.
-- No schema change or host restart. A new/changed head or ambiguous evidence rejects execution.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
IF DB_NAME()<>N'TraderDB' THROW 54800,'Wrong database.',1;
DECLARE @Revision bigint=$(ExpectedRevision), @Hash varchar(64)='$(ExpectedHeadHash)';
DECLARE @Binding uniqueidentifier,@Root nvarchar(max),@Account nvarchar(max),@AccountHash char(64);
IF (SELECT COUNT(DISTINCT BindingId) FROM dbo.BrokerManagedSessionRevision)<>1
 THROW 54801,'Exactly one reviewed current broker binding is required.',1;
SELECT TOP(1) @Binding=BindingId,@Root=SnapshotJson
FROM dbo.BrokerManagedSessionRevision WITH(UPDLOCK,HOLDLOCK)
WHERE Revision=@Revision AND ContentHash=@Hash COLLATE Latin1_General_100_BIN2;
IF @Root IS NULL OR EXISTS(SELECT 1 FROM dbo.BrokerManagedSessionRevision WHERE BindingId=@Binding AND Revision>@Revision)
 THROW 54802,'The reviewed broker head changed.',1;
SET @AccountHash=JSON_VALUE(@Root,'$.Account."$engineRef"');
SELECT @Account=ContentJson FROM dbo.EngineEvidenceContent WHERE ContentHash=@AccountHash;
IF @Account IS NULL OR CONVERT(char(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),@Account COLLATE Latin1_General_100_BIN2_UTF8)),2)<>@AccountHash
 THROW 54803,'Current account content does not match its retained checksum.',1;
DECLARE @Candidates TABLE(Alias nvarchar(20),Symbol nvarchar(20));
INSERT @Candidates
SELECT DISTINCT p.Symbol,h.Symbol
FROM OPENJSON(@Account,'$.Positions') pj
LEFT JOIN dbo.EngineEvidenceContent pc ON pc.ContentHash=JSON_VALUE(pj.value,'$."$engineRef"') COLLATE Latin1_General_100_BIN2
CROSS APPLY OPENJSON(COALESCE(pc.ContentJson,pj.value))
 WITH(PositionId uniqueidentifier,SecurityId nvarchar(256),Symbol nvarchar(20)) p
JOIN dbo.EngineCurrentState obs ON obs.Kind=N'RealObservation' AND obs.OwnerId=@Binding
 AND obs.Revision=@Revision AND obs.SourceHash=@Hash COLLATE Latin1_General_100_BIN2
CROSS APPLY OPENJSON(obs.StateJson,'$.holdings') WITH(Symbol nvarchar(20) '$.symbol',ownerRecordId uniqueidentifier) h
JOIN dbo.ActivePosition t ON t.Symbol=p.Symbol AND t.ExecutionMode=N'Real' AND t.IsActive=1
JOIN dbo.Symbols s ON s.Symbol=h.Symbol AND s.SecurityType=N'Stock' AND (s.ExchangeCode IS NULL OR s.ExchangeCode IN(N'TSX',N'XTSE'))
CROSS APPLY (SELECT TOP(1) PlanJson FROM dbo.BrokerAccountImport WHERE BindingId=@Binding ORDER BY Sequence DESC) latest
CROSS APPLY OPENJSON(latest.PlanJson,'$.Observation.Instruments')
 WITH(SecurityId nvarchar(256),Symbol nvarchar(20),PrimaryMic nvarchar(20),Currency nvarchar(10),SecurityType nvarchar(20)) listing
WHERE h.ownerRecordId=p.PositionId AND p.Symbol=h.Symbol+N'.TO'
 AND listing.SecurityId=p.SecurityId AND listing.Symbol IN(h.Symbol,p.Symbol)
 AND listing.PrimaryMic=N'XTSE' AND listing.Currency=N'CAD' AND listing.SecurityType=N'EQUITY'
 AND obs.StateHash=CONVERT(char(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),obs.StateJson COLLATE Latin1_General_100_BIN2_UTF8)),2)
 AND (pc.ContentJson IS NULL OR pc.ContentHash=CONVERT(char(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),pc.ContentJson COLLATE Latin1_General_100_BIN2_UTF8)),2))
 AND NOT EXISTS(SELECT 1 FROM dbo.Symbols x WHERE x.Symbol=p.Symbol)
 AND NOT EXISTS(SELECT 1 FROM dbo.SymbolAliases x WHERE x.Source=N'Broker' AND x.Alias=p.Symbol);
IF (SELECT COUNT(*) FROM @Candidates)<>1 THROW 54804,'Exactly one missing alias with matching saved listing and holding evidence is required.',1;
INSERT dbo.SymbolAliases(Source,Alias,Symbol,ExchangeMic,VerifiedUtc)
SELECT N'Broker',Alias,Symbol,N'XTSE',SYSUTCDATETIME() FROM @Candidates;
IF @@ROWCOUNT<>1 THROW 54805,'Expected one additive alias.',1;
COMMIT TRANSACTION;
SELECT 1 AS AddedAliases;
