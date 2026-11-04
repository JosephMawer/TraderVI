#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.WSTrade.Orders;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

// Opt-in only. The tested repositories receive a marker-checking LocalDB connection factory.
// No normal host, broker/session store, market client, production connection or installed path is used.
public sealed class PaperRestartCopyTests
{
    private static readonly Guid OriginalRecovery = new("28f8f730-76ce-471f-94d2-cde1904cef0d");
    [PaperCopyFact]
    public async Task RehearseCopiedPaperState()
    {
        var token = Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_PAPER_REHEARSAL")!, "N");
        string phase = Environment.GetEnvironmentVariable("TRADERVI_PAPER_PHASE")!;
        Assert.Contains(phase, new[] { "adopt", "restart", "append", "audit", "resume", "negative" });
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        string folder = Path.Combine(root!.FullName, "artifacts", "paper-rehearsal", token.ToString("N"));
        Assert.Contains("restored", await File.ReadAllTextAsync(Path.Combine(folder, "copy.json")));
        using var ownership = new EngineOwnershipLease(Path.Combine(folder, "rehearsal.lock"));
        using var trace = new SqlTrace();
        var timer = Stopwatch.StartNew();
        var sql = new RestartRehearsalDatabase(token);
        var integrity = new EngineIntegrityRepository(sql.OpenAsync);
        var paper = new EnginePaperVerificationRepository(sql.OpenAsync);
        var witness = new EngineContinuityRepository(sql.OpenAsync);
        var ledger = new DelphiLiveLedgerRepository(workingState: true, connectionFactory: sql.OpenAsync);
        await using var c = await sql.OpenAsync();
        Assert.True(await EngineIntegrityRepository.RequiredGuardsAsync(c, null, default));
        var before = (await c.QueryAsync<Head>("SELECT PortfolioId,Revision,SnapshotJson FROM dbo.DelphiLivePortfolioLedger;")).ToArray();
        var auditBefore = (await integrity.ReadStatusAsync()).Runs;
        var details = new Dictionary<string, object?> { ["Phase"] = phase, ["PortfolioCount"] = before.Length };
        if (phase == "adopt")
        {
            Assert.Empty(await EngineIntegrityRepository.ReadOpenAsync(c, null, default));
            // A restored database is deliberately a different identity. First prove that the
            // authentic production proof is rejected, then add a clearly synthetic clone proof.
            await Assert.ThrowsAsync<InvalidDataException>(() => paper.PrepareAsync(OriginalRecovery));
            var original = EngineIntegrityRepository.Decode(await c.QuerySingleAsync<EngineIntegrityRepository.RunRow>(
                "SELECT * FROM dbo.EngineAuditRun WHERE RunId=@Id;", new { Id = OriginalRecovery }));
            var observed = await witness.ReadAsync(default);
            Assert.NotEqual(original.Plan.Database, observed.Database);
            var proof = original with { RunId = Guid.NewGuid(), Plan = original.Plan with { Database = observed.Database } };
            await c.ExecuteAsync("""
INSERT dbo.EngineAuditRun(RunId,Kind,BoundaryUtc,PlanJson,PlanHash,ProgressJson,ProgressHash,Revision,Status,UpdatedUtc)
VALUES(@RunId,@Kind,@BoundaryUtc,@PlanJson,@PlanHash,@ProgressJson,@ProgressHash,@Revision,@Status,@UpdatedUtc);
""", EngineIntegrityRepository.Encode(proof));
            await c.ExecuteAsync("""
DECLARE @Id uniqueidentifier=NEWID();
INSERT dbo.EngineIntegrityIncident(IncidentId,Scope,OwnerId,EvidenceKey,FailureCode,ProtectionUntrusted)
VALUES(@Id,N'Paper',NULL,N'IsolatedCopyProof',N'SyntheticCopyOnly',1);
INSERT dbo.EngineIntegrityResolution(IncidentId,RecoveryRunId,VerifiedHeadsHash,OperatorName,Reason)
SELECT @Id,@Run,VerifiedHeadsHash,N'Isolated rehearsal',N'Synthetic identity mapping of copied completed proof; no production authority'
FROM (SELECT TOP(1) VerifiedHeadsHash FROM dbo.EngineIntegrityResolution WHERE RecoveryRunId=@Original) r;
""", new { Run = proof.RunId, Original = OriginalRecovery });
            await paper.PrepareAsync(proof.RunId);
            var after = (await c.QueryAsync<Head>("SELECT PortfolioId,Revision,SnapshotJson FROM dbo.DelphiLivePortfolioLedger;")).ToArray();
            Assert.Equal(before.OrderBy(x => x.PortfolioId), after.OrderBy(x => x.PortfolioId));
            Assert.Equal(before.Length, await c.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.EngineCurrentState WHERE Kind=N'PaperVerifiedBoundary';"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => paper.PrepareAsync(proof.RunId));
            var paired = observed.Stamp! with { Database = observed.Database, Sequence = observed.Stamp!.Sequence + 1, Clean = true };
            await witness.WriteAsync(observed.Stamp, paired, default);
            new EngineContinuityFile(Path.Combine(folder, "witness.dat"), new CopyProtector()).Write(paired);
            details["OriginalRestoreIdentityRejected"] = true;
            details["PortfolioHeadsUnchanged"] = true;
            details["SyntheticCopyProof"] = proof.RunId;
            details["TailRevisions"] = before.Sum(h => h.Revision - (original.Plan.Targets.SingleOrDefault(t => t.OwnerId == h.PortfolioId)?.ThroughRevision ?? -1));
        }
        else
        {
            var continuity = new EngineContinuity(witness, new EngineContinuityFile(Path.Combine(folder, "witness.dat"), new CopyProtector()));
            await continuity.StartAsync();
            trace.Commands.Clear();
            await paper.QualifyRestartAsync(continuity.StartupWitnessMatched);
            Assert.Equal(before.Length, PaperRestartQualification.Current!.Portfolios.Length);
            Assert.NotEmpty(trace.Commands);
            Assert.DoesNotContain(trace.Commands, q => q.Contains("FROM dbo.DelphiLiveSnapshotItem", StringComparison.OrdinalIgnoreCase) ||
                q.Contains("FROM [dbo].[DelphiLiveSnapshotItem]", StringComparison.OrdinalIgnoreCase));
            details["RestartMilliseconds"] = timer.Elapsed.TotalMilliseconds;
            details["RestartSqlCommands"] = trace.Commands.Count;
            details["RestartReadArchiveItems"] = false;
            if (phase == "restart")
            {
                var run = await integrity.StartAsync(EngineAuditKind.NewHistory, scope: EngineRecoveryScope.Paper);
                Assert.All(run.Plan.Targets, t => Assert.Equal(t.ThroughRevision + 1, t.FromRevision));
                while (!run.Progress.Complete) run = await paper.VerifyNextAsync(run);
                Assert.Equal(0, run.Progress.VerifiedUnits);
                details["NoNewHistoryVerifiedUnits"] = 0;
            }
            if (phase == "append")
            {
                var lease = await Lease(c);
                var selected = before.OrderByDescending(x => x.Revision).First();
                var state = (await ledger.LoadPortfolioAsync(selected.PortfolioId))!;
                string cashAndHoldings = Money(state);
                var next = state with { Revision = state.Revision + 1, LastCompletedCycleId = Guid.NewGuid() };
                var events = new[] { new DelphiLiveLedgerEvent(Guid.NewGuid(), "PaperCopyRehearsal", DateTime.UtcNow, "{}") };
                await ledger.CommitAsync(state.Revision, next, events, lease);
                // Deliberately discard the first acknowledgement, then retry the exact original request.
                await ledger.CommitAsync(state.Revision, next, events, lease);
                var readback = (await ledger.LoadPortfolioAsync(state.PortfolioId))!;
                Assert.Equal(next.Revision, readback.Revision); Assert.Equal(cashAndHoldings, Money(readback));
                var mark = new DelphiLiveLedgerMark(Guid.NewGuid(), readback.CurrentSession ?? readback.EffectiveSession,
                    DelphiLivePortfolioMarkKind.Checkpoint, DateTime.UtcNow, true, readback.Cash, [], "Isolated verification rehearsal");
                var marked = readback with { Revision = readback.Revision + 1, Marks = readback.Marks.Add(mark) };
                await ledger.CommitAsync(readback.Revision, marked,
                    [new DelphiLiveLedgerEvent(Guid.NewGuid(), "PaperCopyRehearsalMark", DateTime.UtcNow, "{}")], lease);
                details["NewRevisions"] = 2; details["DuplicateRetryAppends"] = 0;
            }
            if (phase is "audit" or "resume")
            {
                var run = await integrity.StartAsync(EngineAuditKind.NewHistory, scope: EngineRecoveryScope.Paper);
                if (phase == "audit")
                {
                    Assert.Equal(2, run.Plan.Targets.Sum(t => t.ThroughRevision - t.FromRevision + 1));
                    while (!run.Progress.Complete && run.Progress.NextRevision > run.Plan.Targets[run.Progress.TargetIndex].ThroughRevision)
                        run = await paper.VerifyNextAsync(run);
                    var receipts = (await c.QueryAsync<string>("SELECT StateJson FROM dbo.EngineCurrentState WHERE Kind=N'PaperVerifiedBoundary' ORDER BY OwnerId;")).ToArray();
                    await c.ExecuteAsync("""
CREATE TRIGGER dbo.PaperRehearsalRollback ON dbo.EngineAuditRun AFTER UPDATE AS
THROW 54801,'Injected rehearsal transaction failure',1;
""");
                    try { await Assert.ThrowsAsync<SqlException>(() => paper.VerifyNextAsync(run)); }
                    finally { await c.ExecuteAsync("DROP TRIGGER dbo.PaperRehearsalRollback;"); }
                    Assert.Equal(receipts, (await c.QueryAsync<string>("SELECT StateJson FROM dbo.EngineCurrentState WHERE Kind=N'PaperVerifiedBoundary' ORDER BY OwnerId;")).ToArray());
                    Assert.Equal(JsonSerializer.Serialize(run), JsonSerializer.Serialize(await ReadRun(c, run.RunId)));
                    await paper.VerifyNextAsync(run); // Discard returned progress; next test process must read durable pair.
                    details["RollbackKeptBoundaryAndCursor"] = true;
                    details["DiscardedCommitReply"] = true;
                }
                else
                {
                    Assert.Equal(1, run.Progress.VerifiedUnits);
                    while (!run.Progress.Complete) run = await paper.VerifyNextAsync(run);
                    Assert.Equal(2, run.Progress.VerifiedUnits);
                    details["TotalNewHistoryVerifiedUnits"] = 2;
                    var empty = await integrity.StartAsync(EngineAuditKind.NewHistory, scope: EngineRecoveryScope.Paper);
                    Assert.All(empty.Plan.Targets, t => Assert.Equal(t.ThroughRevision + 1, t.FromRevision));
                    while (!empty.Progress.Complete) empty = await paper.VerifyNextAsync(empty);
                    Assert.Equal(0, empty.Progress.VerifiedUnits);
                    details["RepeatedAuditVerifiedUnits"] = 0;
                }
            }
            if (phase == "negative")
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => paper.QualifyRestartAsync(false));
                var bad = before.OrderBy(x => x.PortfolioId).First().PortfolioId;
                string original = await c.QuerySingleAsync<string>("SELECT StateJson FROM dbo.EngineCurrentState WHERE Kind=N'PaperWorking' AND OwnerId=@bad;", new { bad });
                await c.ExecuteAsync("UPDATE dbo.EngineCurrentState SET StateJson=StateJson+N' ' WHERE Kind=N'PaperWorking' AND OwnerId=@bad;", new { bad });
                await paper.QualifyRestartAsync(true);
                Assert.DoesNotContain(bad, PaperRestartQualification.Current!.Portfolios);
                Assert.Equal(before.Length - 1, PaperRestartQualification.Current.Portfolios.Length);
                Assert.False((await integrity.ReadAccessAsync(EngineIntegrityScope.Paper, bad)).ProtectionAllowed);
                var good = before.First(h => h.PortfolioId != bad).PortfolioId;
                Assert.True((await integrity.ReadAccessAsync(EngineIntegrityScope.Paper, good)).ProtectionAllowed);
                await c.ExecuteAsync("UPDATE dbo.EngineCurrentState SET StateJson=@original WHERE Kind=N'PaperWorking' AND OwnerId=@bad;", new { bad, original });
                await paper.QualifyRestartAsync(true);
                Assert.Equal(before.Length, PaperRestartQualification.Current!.Portfolios.Length);
                details["MissingContinuityRejected"] = true;
                details["DamagedCheckpointIsolated"] = true;
                details["OriginalCheckpointRequalified"] = true;
            }
            var afterAudits = (await integrity.ReadStatusAsync()).Runs;
            Assert.DoesNotContain(afterAudits, r => r.Plan.Kind is EngineAuditKind.Recovery or EngineAuditKind.Archive && !auditBefore.Any(a => a.RunId == r.RunId));
            await continuity.CloseAsync();
        }
        details["ElapsedMilliseconds"] = timer.Elapsed.TotalMilliseconds;
        details["BrokerClientConstructed"] = false;
        details["DatabaseInstance"] = "LocalDB token-marked copy";
        await File.WriteAllTextAsync(Path.Combine(folder, phase + "-passed.json"), JsonSerializer.Serialize(details, new JsonSerializerOptions { WriteIndented = true }));
        PaperRestartQualification.Set(null);
    }

    private static string Money(DelphiLivePortfolioSnapshot state) => DelphiLiveLedgerJson.Serialize(new { state.Cash, Positions = state.OpenPositions, state.Fills, state.PendingActions });
    private static async Task<EngineAuditRun> ReadRun(SqlConnection c, Guid id) => EngineIntegrityRepository.Decode(
        await c.QuerySingleAsync<EngineIntegrityRepository.RunRow>("SELECT * FROM dbo.EngineAuditRun WHERE RunId=@id;", new { id }));
    private static async Task<DelphiLiveLease> Lease(SqlConnection c)
    {
        await c.ExecuteAsync("UPDATE dbo.DelphiLiveHostLease SET IsHeld=0,ReleasedUtc=SYSUTCDATETIME() WHERE IsHeld=1;");
        var now = DateTime.UtcNow; var id = Guid.NewGuid();
        long fence = await c.QuerySingleAsync<long>("SELECT ISNULL(MAX(FencingToken),0)+1 FROM dbo.DelphiLiveHostLease;");
        var lease = new DelphiLiveLease(id, "PaperCopyRehearsal", fence, now, now.AddHours(2));
        await c.ExecuteAsync("""
INSERT dbo.DelphiLiveHostLease(LeaseId,LeaseName,OwnerId,FencingToken,CollectorVersion,SourceContractVersion,CodeCommit,WorkingTreeState,AcquiredUtc,LastRenewedUtc,ExpiresUtc,IsHeld)
VALUES(@LeaseId,N'DelphiLiveMonitor',@OwnerId,@FencingToken,N'Rehearsal',1,N'REHEARSAL',N'Unknown',@AcquiredUtc,@AcquiredUtc,@ExpiresUtc,1);
""", lease);
        return lease;
    }
    private sealed record Head(Guid PortfolioId, long Revision, string SnapshotJson);
    private sealed class CopyProtector : IManualBuyProtector
    { public byte[] Protect(byte[] value) => value.ToArray(); public byte[] Unprotect(byte[] value) => value.ToArray(); }
    private sealed class PaperCopyFact : FactAttribute
    {
        public PaperCopyFact()
        { if (!Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_PAPER_REHEARSAL"), "N", out _)) Skip = "Requires explicit isolated-copy rehearsal authority and token."; }
    }
    private sealed class SqlTrace : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> subscriptions = [];
        public List<string> Commands { get; } = [];
        public SqlTrace() => subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        public void OnNext(DiagnosticListener value) { if (value.Name == "SqlClientDiagnosticListener") subscriptions.Add(value.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        { if (value.Key.EndsWith("WriteCommandBefore") && value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is SqlCommand cmd) Commands.Add(cmd.CommandText); }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose() { foreach (var value in subscriptions) value.Dispose(); }
    }
}
