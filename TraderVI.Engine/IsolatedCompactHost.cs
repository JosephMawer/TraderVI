#if COMPACT_RESTART_CANDIDATE
using Core.Db;
using Core.Runtime;
using Core.Trader.Storage;
using Core.Trader.Promotion;
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using Dapper;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Services;

namespace TraderVI.Engine;

// Compiled in place of the installed --run composition. The only broker inputs are fixture observations.
// This binary opens no live broker, configured SQL connection, installed journal, RPC endpoint or auxiliary workers.
internal static class IsolatedCompactHost
{
    private sealed record Holding(string Security, string Symbol, int Quantity);
    private sealed record BrokerCopy(Guid Binding, string Account, DateTimeOffset At, decimal Cash, decimal BuyingPower, Holding[] Holdings);
    private sealed class MarketTime(DateTimeOffset at) : TimeProvider { public override DateTimeOffset GetUtcNow() => at; }

    public static async Task<int> RunAsync()
    {
        var elapsed = Stopwatch.StartNew();
        if (!Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_CANDIDATE_REHEARSAL"), "N", out var token) || token == Guid.Empty) return 64;
        string step = "ownership";
        try
        {
            var folder = Path.GetFullPath(Path.Combine("artifacts", "restart-release", token.ToString("N")));
            using var owner = new EngineOwnershipLease(Path.Combine(folder, "engine.lease"));
            var sql = new RestartRehearsalDatabase(token);
            var protector = new WindowsManualBuyProtector();
            using var journal = new ManualBuyFileJournal(folder, protector);
            var grants = new BrokerExecutionHostGrantFileStore(Path.Combine(folder, "synthetic-grant.dat"), protector);
            var store = new SqlTradingStorageDatabase(sql.OpenAsync).CreateStore();
            var witnesses = new WitnessStore(sql);
            var continuity = new EngineContinuity(witnesses, new EngineContinuityFile(Path.Combine(folder, "witness.dat"), protector));
            var frozen = JsonSerializer.Deserialize<BrokerCopy>(File.ReadAllText(Path.Combine(folder, "broker-copy.json"))) ?? throw new InvalidDataException();
            var calendar = ReviewedTsxSessionCalendar.Load(Path.Combine(folder, "session-calendar.json"));
            var time = new MarketTime(frozen.At);
            var build = BrokerManagedSessionComposition.ImplementationIdentity;
            CompactDelphiCheckpoint? checkpoint = null;
            string Slot(long version) => Path.Combine(folder, $"compact-{version % 2}.dat");
            async Task<bool> Qualify(CancellationToken ct)
            {
                step = "compact checkpoint";
                var state = await store.ReadAsync(frozen.Binding, ct) ?? throw new InvalidDataException();
                var db = await witnesses.ReadAsync(ct);
                checkpoint = CompactDelphiCheckpoint.Read(Slot(state.Account.Version), protector);
                if (!checkpoint.Matches(token, db.Database, state)) return false;
                return checkpoint.Assignment.Account == frozen.Account;
            }
            async Task StartContinuity()
            {
                step = "continuity";
                await continuity.StartAsync(Qualify, default);
                if (checkpoint == null) throw new InvalidOperationException("Initialization did not qualify its compact checkpoint.");
            }
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task TradingCycle()
            {
                try
                {
                    step = "verify active continuity witness";
                    await continuity.VerifyAsync();
                    step = "read qualified account";
                    var sealedState = checkpoint ?? throw new InvalidDataException();
                    var assignment = sealedState.Assignment;
                    var initial = await store.ReadAsync(frozen.Binding) ?? throw new InvalidDataException();
                    step = "validate trading boundary";
                    if (!sealedState.Matches(token, sealedState.Database, initial)) throw new InvalidDataException();
                    assignment.RequireAssignment(initial, grants.Read(), build, frozen.At);
                    var buyIds = initial.Orders.Where(o => o.Side == "BUY").Select(o => "order-" + o.RequestId.ToString("D")).ToHashSet();
                    var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(frozen.At, ReviewedTsxSessionCalendar.Toronto).DateTime);
                    if (!calendar.IsRegularSession(date)) throw new InvalidOperationException("Outside regular session.");
                    var bounds = calendar.GetSessionBounds(date);
                    if (frozen.At < bounds.OpenUtc || frozen.At >= bounds.CloseUtc) throw new InvalidOperationException("Outside regular hours.");
                    var committing = store.WithCheckpointWriter(next =>
                    {
                        CompactDelphiCheckpoint.From(token, sealedState.Database, assignment, next).Write(Slot(next.Account.Version), protector);
                        if (Environment.GetEnvironmentVariable("TRADERVI_REHEARSAL_INTERRUPT") == "before-sql-commit" &&
                            next.Checkpoints.Single(c => c.Version == next.Account.Version).OperationKind == "ReserveProtectiveExit")
                        {
                            Console.WriteLine("PROTECTIVE_RESERVATION_BEFORE_SQL_COMMIT");
                            Console.Out.Flush();
                            if (Console.ReadLine() != "continue") throw new IOException("Interrupted before SQL commit.");
                        }
                    });
                    int eligible = 0, buyReads = 0, accountReads = 0;
                    step = "reconcile active orders";
                    var result = await assignment.RecoverAsync(committing, journal, grants, build, time,
                        (binding, id, _) =>
                        {
                            if (binding != frozen.Binding) throw new InvalidDataException();
                            if (buyIds.Contains(id)) buyReads++;
                            return Task.FromResult<BrokerOrderFact?>(null); // No conclusive broker reply in this scenario.
                        },
                        (binding, _) =>
                        {
                            if (binding != frozen.Binding) throw new InvalidDataException();
                            accountReads++;
                            var snapshot = new WealthsimpleTfsaSnapshot(new(frozen.Account, "Synthetic"), frozen.At, null,
                                new(frozen.Cash, "CAD"), new(frozen.BuyingPower, "CAD"), null, null, null, null, null,
                                frozen.Holdings.Select(h => new WealthsimpleHolding(h.Symbol, "Synthetic", "EQUITY", h.Quantity, null, null, null)
                                    { SecurityId = h.Security }).ToArray());
                            return Task.FromResult((binding, snapshot));
                        }, sale =>
                        {
                            if (sale.Side != "SELL") throw new InvalidDataException();
                            eligible++;
                        }, default);
                    bool sameBuys = initial.Orders.Where(o => o.Side == "BUY").OrderBy(o => o.RequestId)
                        .SequenceEqual(result.Current.Orders.Where(o => o.Side == "BUY").OrderBy(o => o.RequestId));
                    step = "validate resulting checkpoint";
                    var next = CompactDelphiCheckpoint.Read(Slot(result.Current.Account.Version), protector);
                    if (!next.Matches(token, sealedState.Database, result.Current)) throw new InvalidDataException();
                    bool ready = accountReads == 1 && result.Current.Restrictions.IsEmpty &&
                        result.Notices.All(n => result.Current.Orders.Any(o => o.RequestId == n.OriginalId));
                    Console.WriteLine(JsonSerializer.Serialize(new { ready, interrupted = continuity.ResumedInterruptedRun,
                        reserved = result.Current.Account.ReservedPurchaseCost, booked = result.Current.Account.CommittedPurchaseCost,
                        sellShares = result.Current.Orders.Where(o => o.Side == "SELL").Sum(o => o.ReservedQuantity),
                        sameBuys, eligible, buyReads, accountReads, submissions = 0,
                        symbolRows = result.Current.Symbols.Length, readyMs = elapsed.Elapsed.TotalMilliseconds }));
                    Console.Out.Flush();
                    done.TrySetResult();
                }
                catch (Exception e) { done.TrySetException(e); }
            }
            var engine = new IndependentEngine([new("Real trading", TimeSpan.FromDays(1), TradingCycle)],
                () => IndependentEngine.InitializeHostAsync(StartContinuity, () => Task.CompletedTask, () => Task.CompletedTask, false, false),
                () => continuity.CloseAsync());
            await engine.StartAsync();
            await done.Task;
            await engine.StopAsync();
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { ready = false, step, error = e.GetType().Name }));
            return 1;
        }
    }

    private sealed class WitnessStore(RestartRehearsalDatabase db) : IEngineContinuityStore
    {
        public async Task<EngineContinuityObservation> ReadAsync(CancellationToken ct)
        {
            await using var c = await db.OpenAsync(ct);
            if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.RehearsalHold") != 0)
                throw new InvalidDataException("Existing recovery hold.");
            var value = await c.QuerySingleOrDefaultAsync<string>("SELECT Stamp FROM dbo.RehearsalWitness WHERE Id=1");
            return new(await EngineContinuityRepository.IdentityAsync(c, null!, ct),
                value == null ? null : JsonSerializer.Deserialize<EngineContinuityStamp>(value));
        }
        public async Task WriteAsync(EngineContinuityStamp? expected, EngineContinuityStamp next, CancellationToken ct)
        {
            await using var c = await db.OpenAsync(ct);
            if (await c.ExecuteAsync("UPDATE dbo.RehearsalWitness SET Stamp=@Next WHERE Id=1 AND Stamp=@Previous",
                new { Next = next.Json, Previous = expected?.Json }) != 1) throw new InvalidDataException("Witness changed.");
        }
        public async Task HoldAsync(string key, string code, CancellationToken ct)
        {
            await using var c = await db.OpenAsync(ct);
            await c.ExecuteAsync("INSERT dbo.RehearsalHold(Code) VALUES (@code)", new { code });
            throw new InvalidDataException("Recovery required.");
        }
    }
}
#endif
