#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using Dapper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    // Separate test processes stage once, start once and restart the same book.
    // Every SQL connection is restricted to the already marked disposable LocalDB.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [NormalStartupFact]
    public async Task NormalTradingStartupWithAnUncertainBuy()
    {
        var token = Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_NORMAL_REHEARSAL")!, "N");
        var sql = new RestartRehearsalDatabase(token);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TraderVI.sln"))) root = root.Parent;
        var folder = Path.Combine(root!.FullName, "artifacts", "restart-release", token.ToString("N"));
        var phase = Environment.GetEnvironmentVariable("TRADERVI_NORMAL_PHASE");
        Assert.Contains(phase, new[] { "prepare", "first", "retry" });
        var clock = Stopwatch.StartNew();
        using var owner = new EngineOwnershipLease(Path.Combine(folder, "engine.lease"));
        var protector = new RehearsalProtector();
        var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(folder, "synthetic-grant.dat"), protector);
        var file = new EngineContinuityFile(Path.Combine(folder, "witness.dat"), protector);
        var witness = new Witness(sql);
        var store = new SqlTradingStorageDatabase(sql.OpenAsync).CreateStore();
        using var journal = new ManualBuyFileJournal(folder, protector);
        string sourceFile = Path.Combine(folder, "synthetic-session.json");
        if (phase == "prepare")
        {
            await PrepareNormalStartupAsync(sql, store, grants, journal, file, sourceFile);
            return;
        }

        var source = JsonSerializer.Deserialize<BrokerManagedSession>(await File.ReadAllTextAsync(sourceFile))!;
        var originalBuy = source.Orders.Single();
        var exit = source.Protection.Single().FirstExit!;
        var requestId = BrokerManagedStrategy.StableId(exit.DecisionId.ToString("D"));
        var heldPosition = source.Account.Positions.Single();
        var sessions = new BrokerManagedSessionTests.Store { State = source };
        var continuity = new EngineContinuity(witness, file);
        var time = new RehearsalTime(source.SavedAt);
        var replay = new RehearsalReads(source, time);
        var connection = new EngineBrokerClient(replay, protector);
        var calendar = new ReviewedTsxSessionCalendar(new("fixture", "offline", new(2026,9,22), new(2026,9,25), [new(2026,9,23)]));
        AccountRestartResult? decision = null;
        int eligible = 0;
        var lookups = new List<string>();
        var broker = new EngineBrokerService(connection, journal, protector, grants, calendar, (_, ct) => continuity.VerifyAsync(ct),
            sessions, source.Strategy.Strategy.ImplementationIdentity, false, async (saved, selected, orderGate, ct) =>
            {
                var buyJournal = new RestartBuyJournal(journal, saved.BindingId, saved.Account.Import.Plan.Observation.IdentityId, selected.Id);
                async Task<IRestartSellLease> Acquire(CancellationToken cancellation)
                {
                    await orderGate.WaitAsync(cancellation);
                    return new RestartSellAdmission(saved, grants, journal, saved.Strategy.Strategy.ImplementationIdentity, new ReleaseGate(orderGate));
                }
                decision = await new OfflineAccountRestart(store, (_, id, _) =>
                {
                    lookups.Add(id);
                    // Read-only fixture has no conclusive reply for either original request.
                    return Task.FromResult<BrokerOrderFact?>(null);
                }, async (binding, cancellation) =>
                {
                    var observed = await connection.ReadTfsaSnapshotAsync(selected, cancellation);
                    Assert.Equal(WealthsimpleReadStatus.Success, observed.Status);
                    return (binding, observed.Data!);
                }, (sale, _) =>
                {
                    Assert.Equal("SELL", sale.Side);
                    Assert.Equal(requestId, sale.RequestId);
                    Assert.Equal(exit.DecisionId, sale.DecisionId);
                    Assert.Equal(heldPosition.SecurityId, sale.SecurityId);
                    Assert.NotEqual(originalBuy.SecurityId, sale.SecurityId);
                    eligible++; // Observe after the final admission check. There is no submit client.
                    return Task.CompletedTask;
                }, Acquire, (_, _) => throw new InvalidOperationException("No fill to open."), time, buyJournal).RunAsync(saved.BindingId, ct);
                return saved;
            }, time);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new IndependentEngine([new("Real trading", TimeSpan.FromSeconds(1), async () =>
        {
            await broker.PollExecutionAsync();
            completed.TrySetResult();
        })], () => IndependentEngine.InitializeHostAsync(continuity, broker, () => Task.CompletedTask, false, false),
        async () => { await broker.CloseAsync(); await continuity.CloseAsync(); });
        try
        {
            await engine.StartAsync();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var elapsed = clock.Elapsed;
            Assert.NotNull(decision);
            var book = decision!.Current;
            Assert.Empty(book.Restrictions);
            Assert.False(book.Account.NewBuysStopped);
            Assert.True(book.Settings.SellAllowed);
            Assert.Equal(100m, book.Account.ReservedPurchaseCost);
            Assert.Equal(0m, book.Account.CommittedPurchaseCost);
            var buy = Assert.Single(book.Orders.Where(o => o.Side == "BUY"));
            Assert.Equal(originalBuy.RequestId, buy.RequestId);
            Assert.Equal(100m, buy.ReservedCash);
            Assert.Equal("Unknown", buy.Status);
            var sale = Assert.Single(book.Orders.Where(o => o.Side == "SELL"));
            Assert.Equal(requestId, sale.RequestId);
            Assert.Equal(heldPosition.Quantity, sale.ReservedQuantity);
            Assert.Equal(heldPosition.Quantity, book.Positions.Single().ReservedSellQuantity);
            Assert.Equal(phase == "first" ? 1 : 0, eligible);
            Assert.Equal(new[] { "order-" + requestId, originalBuy.ExternalId }, lookups);
            Assert.Contains(decision.Notices, n => n.OriginalId == buy.RequestId);
            Assert.DoesNotContain(decision.Notices, n => n.Action is "Account observation" or "Holdings");
            Assert.Equal(1, replay.Snapshots);
            Assert.Equal(0, replay.UnexpectedCalls);
            output.WriteLine($"{phase}: host start through first completed trading decision {elapsed.TotalMilliseconds:F1} ms; original buy $100 reserved, $0 booked; {sale.ReservedQuantity} sell shares reserved; {eligible} new sell admissions; zero broker submissions.");
        }
        finally { broker.BeginStop(); await engine.StopAsync(); }
    }

    private async Task PrepareNormalStartupAsync(RestartRehearsalDatabase sql, TradingCurrentStore store,
        BrokerExecutionHostGrantFileStore grants, ManualBuyFileJournal journal, EngineContinuityFile file, string sourceFile)
    {
        var fixture = await CrashAfterProtectiveTrigger();
        var saved = fixture.Store.State!;
        var bound = saved.BindingId;
        var approval = saved.EffectiveReview;
        var held = saved.Account.Positions.Single();
        var protection = saved.Protection.Single();
        var at = fixture.Now;
        var original = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-BBB", "BBB", "BUY",
            10, 10m, "Initial", null, at, WealthsimpleSubmissionStatus.OutcomeUnknown);
        saved = saved with { Orders = [original] };
        await File.WriteAllTextAsync(sourceFile, JsonSerializer.Serialize(saved));
        fixture.Store.State = saved;
        fixture.Transport.BeforePrepare = () => throw new InvalidOperationException("Disabled rehearsal transport.");
        var legacy = await fixture.Workflow().TickAsync(bound);
        Assert.Equal("OrderRecoveryBlocked", legacy.Event);
        Assert.Equal(100m, legacy.CommittedPurchaseCost);
        Assert.Equal(0, fixture.Transport.Sent);
        var account = Account with { BindingId = bound, PermissionId = approval.RequestId, StrategyId = saved.Strategy.VersionId,
            AssignmentId = saved.SessionId, BaselineId = approval.BaselineId, ExecutionEnabled = true,
            TradingDate = at.UtcDateTime.Date, RiskThroughUtc = at.UtcDateTime, NewBuysStopped = false,
            DailyLossHold = false, CapitalReviewRequired = false };
        var settings = Settings with { BindingId = bound, PermissionId = approval.RequestId, StrategyId = account.StrategyId,
            AssignmentId = account.AssignmentId, BaselineId = account.BaselineId, ImplementationIdentity = fixture.ImplementationIdentity,
            SourceId = saved.Strategy.PaperAccountId, PurchaseCeiling = approval.PurchaseCeiling,
            ApprovedUtc = approval.PreparedAt.UtcDateTime, BuyAllowed = true, SellAllowed = true };
        var p = Position() with { BindingId = bound, PositionId = held.PositionId, SecurityId = held.SecurityId,
            Symbol = held.Symbol, Quantity = held.Quantity, CostBasis = held.CostBasis, FirstEntryUtc = held.EntryCompletedAt.UtcDateTime,
            LastEntryUtc = held.EntryCompletedAt.UtcDateTime, ProtectionOwner = protection.Owner,
            ProtectionFloor = saved.Memory.Symbols[held.Symbol].LiveProtection!.Progressive!.StopPrice };
        var buy = InterruptedOrder with { BindingId = bound, PermissionId = approval.RequestId, RequestId = original.RequestId,
            DecisionId = original.DecisionId, SecurityId = original.SecurityId, Symbol = original.Symbol, CreatedUtc = at.UtcDateTime };
        await using var admin = await sql.OpenAsync();
        await admin.ExecuteAsync("""
            INSERT dbo.BrokerAccountImportBinding VALUES (@bound,N'TFSA',REPLICATE('A',64),REPLICATE('B',64),SYSUTCDATETIME(),N'Synthetic rehearsal');
            CREATE TABLE dbo.RehearsalWitness(Id int PRIMARY KEY CHECK(Id=1), Stamp nvarchar(2000) NOT NULL);
            """, new { bound });
        var identity = await EngineContinuityRepository.IdentityAsync(admin, null!, default);
        var stamp = new EngineContinuityStamp(1, identity, Guid.NewGuid(), 0, true);
        await admin.ExecuteAsync("INSERT dbo.RehearsalWitness VALUES (1,@Json)", new { stamp.Json });
        file.Write(stamp);
        await store.SaveAsync(Initial() with { Account = account, Settings = [settings], Positions = [p], Orders = [buy] });
        await RestartSellAdmission.SaveTriggerAsync(store, saved, held.PositionId, at);
        grants.Write(null, new(bound, saved.SessionId, saved.Strategy.Fingerprint, fixture.ImplementationIdentity, at, "Synthetic", true)
            { RestoreAfterRestart = true });
        var observation = saved.Account.Import.Plan.Observation;
        journal.PrepareRestartSellScopes(observation.IdentityId, observation.AccountId, new ManualBuyFileJournal.RestartHolding(p.SecurityId, p.PositionId, p.FirstEntryUtc));
        journal.Save(new(new(buy.RequestId, observation.IdentityId, observation.AccountId, buy.SecurityId, "Synthetic",
            buy.Symbol, "XTSE", 10, new(buy.Symbol, "Synthetic", "Buy"), 10,
            new(10, 9, at, "OPEN", at.AddHours(-1), at.AddHours(1), at), 100, at),
            WealthsimpleSubmissionStatus.OutcomeUnknown, []));
        output.WriteLine("Existing managed workflow: OrderRecoveryBlocked, $100 committed, no sale attempted.");
    }

    private sealed class NormalStartupFactAttribute : FactAttribute
    {
        public NormalStartupFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || !Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_NORMAL_REHEARSAL"), "N", out _))
                Skip = "Requires an explicitly prepared disposable LocalDB and synthetic journal.";
        }
    }
    private sealed class RehearsalTime(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class ReleaseGate(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class RehearsalProtector : IManualBuyProtector, IWealthsimpleSessionProtector
    {
        public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
    }

    private sealed class Witness(RestartRehearsalDatabase db) : IEngineContinuityStore
    {
        public async Task<EngineContinuityObservation> ReadAsync(CancellationToken ct)
        {
            await using var c = await db.OpenAsync(ct);
            return new(await EngineContinuityRepository.IdentityAsync(c, null!, ct),
                JsonSerializer.Deserialize<EngineContinuityStamp>(await c.QuerySingleAsync<string>("SELECT Stamp FROM dbo.RehearsalWitness WHERE Id=1")));
        }
        public async Task WriteAsync(EngineContinuityStamp? expected, EngineContinuityStamp next, CancellationToken ct)
        {
            await using var c = await db.OpenAsync(ct);
            Assert.Equal(1, await c.ExecuteAsync("UPDATE dbo.RehearsalWitness SET Stamp=@New WHERE Id=1 AND Stamp=@Old",
                new { New = next.Json, Old = expected?.Json }));
        }
        public Task HoldAsync(string key, string code, CancellationToken ct) => throw new InvalidOperationException("Unexpected continuity hold: " + code);
    }

    private sealed class RehearsalReads(BrokerManagedSession source, TimeProvider time) : IEngineRpc
    {
        public int UnexpectedCalls, Snapshots;
        public Task<T> CallAsync<T>(EngineOperation operation, object? payload = null, CancellationToken ct = default)
        {
            var account = new WealthsimpleTfsaAccount(source.Account.Import.Plan.Observation.AccountId, "Synthetic");
            object result;
            switch (operation)
            {
                case EngineOperation.Connection:
                    result = new EngineConnectionStatus(DateTimeOffset.UtcNow, true, true, 1, false, false, false, false, "Connected", "Fixture", "");
                    break;
                case EngineOperation.Accounts:
                    result = new WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>(WealthsimpleReadStatus.Success, [account]);
                    break;
                case EngineOperation.AccountSnapshot:
                    Assert.Equal(account.Id, ((WealthsimpleTfsaAccount)payload!).Id);
                    var h = source.Account.Positions.Single();
                    var snapshot = BrokerBook(time.GetUtcNow(), (Position() with { Symbol = h.Symbol, SecurityId = h.SecurityId }, h.Quantity));
                    result = new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success, snapshot with { Account = account });
                    Snapshots++;
                    break;
                default:
                    UnexpectedCalls++;
                    throw new InvalidOperationException("Operation unavailable in read-only rehearsal.");
            }
            return Task.FromResult((T)result);
        }
    }
}
