#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

/// <summary>
/// Opt-in baseline/hold persistence rehearsal. The canonical import/risk tables and triggers live
/// in a new retained synthetic database; minimal report/ledger parents supply synthetic read facts.
/// This does not qualify production import projection, native boundary valuation or broker evidence.
/// </summary>
public sealed class BrokerAccountRiskSqlTests(ITestOutputHelper output)
{
    [NativeBrokerSqlFact]
    [Trait("Category", "NativeSql")]
    public async Task NativeBaselineAndHoldHistoryPreserveReferencesPrecisionImmutabilityAndRetryIdentity()
    {
        string database = "TraderVI_BrokerRisk_" + Guid.NewGuid().ToString("N");
        Assert.Matches("^TraderVI_BrokerRisk_[a-f0-9]{32}$", database);
        string connectionString = new SqlConnectionStringBuilder
        {
            DataSource = "localhost", InitialCatalog = database, IntegratedSecurity = true,
            TrustServerCertificate = true, ApplicationName = database, ConnectTimeout = 10
        }.ConnectionString;
        await using (var administration = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await administration.OpenAsync();
            await administration.ExecuteAsync($"CREATE DATABASE [{database}];");
        }
        output.WriteLine($"Retained synthetic database: {database}. No production account reads/writes or broker client.");
        async Task<SqlConnection> Open(CancellationToken ct)
        {
            var c = new SqlConnection(connectionString);
            try { await c.OpenAsync(ct); Assert.Equal(database, c.Database); return c; }
            catch { await c.DisposeAsync(); throw; }
        }
        await using var control = await Open(default);
        // Parent facts are deliberately narrow: import projection and reported-account persistence
        // have separate qualification. Canonical risk FK, hash and immutable constraints stay intact.
        await control.ExecuteAsync("""
CREATE TABLE dbo.TradingAccountReport (AccountLabel nvarchar(64) NOT NULL, ReportId uniqueidentifier NOT NULL,
 Sequence bigint IDENTITY, ReportJson nvarchar(max), PRIMARY KEY(AccountLabel,ReportId));
CREATE TABLE dbo.TradingRecommendationBatch (AccountLabel nvarchar(64) NOT NULL, BatchId uniqueidentifier NOT NULL,
 Sequence bigint IDENTITY, BatchJson nvarchar(max), PRIMARY KEY(AccountLabel,BatchId));
CREATE TABLE dbo.ActivePosition (PositionId uniqueidentifier,Symbol nvarchar(64),EntryDate datetime2,EntryPrice decimal(18,6),
 Shares int,CostBasis decimal(18,6),IsActive bit,ExecutionMode nvarchar(32),AccountLabel nvarchar(64));
CREATE TABLE dbo.TradeLog (TradeId uniqueidentifier,PositionId uniqueidentifier,Symbol nvarchar(64),TradeType nvarchar(32),
 TradeDate datetime2,Shares int,Price decimal(18,6),Amount decimal(18,6),Commission decimal(18,6),NetAmount decimal(18,6),
 CreatedUtc datetime2,Reason nvarchar(128),ExecutionMode nvarchar(32),AccountLabel nvarchar(64));
CREATE TABLE dbo.TradingReportedFill (TradeId uniqueidentifier,FillUtc datetime2,RecommendationKind nvarchar(64),PositionTarget decimal(18,6));
""");
        foreach (var name in new[] { "BrokerAccountImportBinding", "BrokerAccountImport", "BrokerAccountRiskBaseline", "BrokerAccountRiskContinuation" })
            foreach (var suffix in new[] { ".sql", "_Immutable.sql" })
                foreach (string batch in Regex.Split(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SqlRehearsal", name + suffix)),
                    @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    if (!string.IsNullOrWhiteSpace(batch)) await control.ExecuteAsync(batch);

        var baselines = new BrokerAccountRiskBaselineRepository(Open);
        var empty = await baselines.ReadAsync();
        Assert.True(empty.Installed); Assert.Null(empty.Import.Binding);
        // Preserve sub-millisecond broker evidence through datetime2(7), independently of save time.
        var now = new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero).AddTicks(1234567);
        if (now > DateTimeOffset.UtcNow) now = now.AddSeconds(-1);
        var observation = new BrokerAccountReconciliation("synthetic-identity", "synthetic-account", now.AddSeconds(-1),
            now.AddSeconds(-2), 1000m, 1000m, 1000m, [], [], [], []) { ActivityFingerprint = new string('A', 64) };
        var plan = BrokerAccountImport.Preview(observation, empty.Import, DateTimeOffset.UtcNow);
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        var binding = Guid.NewGuid(); var import = Guid.NewGuid();
        await control.ExecuteAsync("""
INSERT dbo.BrokerAccountImportBinding(BindingId,AccountLabel,BrokerKey,LegacyFingerprint,CreatedUtc,ReviewedBy)
 VALUES(@Binding,N'TFSA',@BrokerKey,@LegacyFingerprint,@CreatedUtc,N'synthetic');
INSERT dbo.BrokerAccountImport(RequestId,BindingId,PriorRequestId,RequestHash,PlanFingerprint,SnapshotUtc,SavedUtc,ReviewedBy,PlanJson)
 VALUES(@Import,@Binding,NULL,@RequestHash,@PlanFingerprint,@SnapshotUtc,@SavedUtc,N'synthetic',@PlanJson);
""", BrokerAccountImportRepository.TimestampParameters(new
        {
            Binding = binding, Import = import, plan.BrokerKey, plan.LegacyFingerprint,
            RequestHash = new string('B', 64), PlanFingerprint = plan.Fingerprint, PlanJson = JsonSerializer.Serialize(plan)
        }, ("CreatedUtc", now.AddMinutes(-1).UtcDateTime), ("SnapshotUtc", observation.SnapshotAt.UtcDateTime), ("SavedUtc", now.UtcDateTime)));
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now.UtcDateTime));
        var report = new ReportedTradingAccount(Guid.NewGuid(), "TFSA", "CAD", date, now.AddSeconds(-5).UtcDateTime,
            "synthetic", empty.Import.Legacy.Fingerprint, 1000m, 1000m, 1100m, 1200m, 0, true, true, []);
        await control.ExecuteAsync("INSERT dbo.TradingAccountReport(AccountLabel,ReportId,ReportJson) VALUES(N'TFSA',@Id,@Json)",
            new { Id = report.SnapshotId, Json = JsonSerializer.Serialize(report) });

        var workflow = new BrokerAccountRiskBaselineWorkflow(baselines);
        var preview = await workflow.PreviewAsync(Guid.NewGuid());
        Assert.True(preview.Preparation.CanEstablish, string.Join("; ", preview.Preparation.Blocks));
        var request = new BrokerRiskBaselineRequest(preview.Preparation.Baseline!, "synthetic");
        var saved = await workflow.ApplyAsync(request);
        var recovered = await new BrokerAccountRiskBaselineRepository(Open).FindAsync(saved.Baseline.BaselineId);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(recovered));
        Assert.Equal(JsonSerializer.Serialize(saved.Baseline), JsonSerializer.Serialize((await baselines.ReadAsync()).RiskHistory.ExistingBaseline));
        Assert.Equal(1234567, recovered!.Baseline.ObservationAt.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal(1100m, recovered.Baseline.SessionReference.Value); Assert.Equal(1200m, recovered.Baseline.DrawdownReference.Value);
        Assert.True(recovered.Baseline.CarriedMemory.DailyLossHold); Assert.True(recovered.Baseline.CarriedMemory.CapitalReviewRequired);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(await workflow.ApplyAsync(request)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(request with { ReviewedBy = "changed" }));
        Assert.Equal(1, await control.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BrokerAccountRiskBaseline"));

        var calendar = new ReviewedTsxSessionCalendar(new("synthetic", "synthetic", date, date.AddDays(1), [date, date.AddDays(1)]));
        var continuing = new BrokerAccountRiskContinuationRepository(calendar, Open);
        var state = await continuing.ReadAsync(saved.Baseline.BaselineId);
        var hold = new BrokerRiskContinuationRequest(Guid.NewGuid(), state.BaselineId, state.RevisionId,
            BrokerRiskContinuationKind.HoldObservation, null, null,
            new(Guid.NewGuid(), binding, date, now, false, false));
        var appended = await continuing.AppendAsync(hold);
        var restart = new BrokerAccountRiskContinuationRepository(calendar, Open);
        Assert.Equal(JsonSerializer.Serialize(appended), JsonSerializer.Serialize(await restart.FindAsync(hold.RequestId)));
        var readback = await restart.ReadAsync(state.BaselineId);
        Assert.Equal(JsonSerializer.Serialize(appended.State), JsonSerializer.Serialize(readback));
        Assert.True(readback.DailyLossHold); Assert.True(readback.CapitalReviewRequired);
        Assert.Equal(state.SessionReference, readback.SessionReference); Assert.Equal(state.DrawdownReference, readback.DrawdownReference);
        Assert.Equal(state.Through, readback.Through);
        Assert.Equal(JsonSerializer.Serialize(appended), JsonSerializer.Serialize(await restart.AppendAsync(hold)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restart.AppendAsync(hold with { Holds = hold.Holds! with { DailyLossHold = true } }));
        var next = hold with { RequestId = Guid.NewGuid(), PreviousRevisionId = hold.RequestId,
            Holds = hold.Holds! with { EvidenceId = Guid.NewGuid() } };
        var latest = await restart.AppendAsync(next);
        Assert.Equal(JsonSerializer.Serialize(latest.State), JsonSerializer.Serialize(await continuing.ReadAsync(state.BaselineId)));
        Assert.True(latest.State.DailyLossHold); Assert.True(latest.State.CapitalReviewRequired);
        Assert.Equal(2, await control.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.BrokerAccountRiskContinuation"));
        var baselineImmutable = await Assert.ThrowsAsync<SqlException>(() => control.ExecuteAsync("DELETE dbo.BrokerAccountRiskBaseline"));
        Assert.Equal(51650, baselineImmutable.Number);
        var historyImmutable = await Assert.ThrowsAsync<SqlException>(() => control.ExecuteAsync("UPDATE dbo.BrokerAccountRiskContinuation SET SavedUtc=SavedUtc"));
        Assert.Equal(51670, historyImmutable.Number);
        output.WriteLine("PASS: actual baseline Save/Find/Read, precise import evidence, retained references/holds, continuation append/restart/retry, canonical immutable triggers. Boundary/cash/broker qualification remains separate.");
    }
}
