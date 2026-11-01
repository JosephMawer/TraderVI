#nullable enable
using Core.Trader.Storage;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests(ITestOutputHelper output)
{
    private static readonly Guid Binding = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Permission = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Strategy = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime Now = new(2026, 10, 30, 15, 0, 0, DateTimeKind.Utc);
    private static TradingAccountCurrent Account => new() { BindingId = Binding, PermissionId = Permission,
        StrategyId = Strategy, AssignmentId = Strategy, BaselineId = Strategy, Phase = "Active", TradingDate = Now.Date,
        RiskEvidenceId = Strategy, RiskThroughUtc = Now, SessionReference = 10000, DrawdownReference = 10000,
        NewBuysStopped = true, DailyLossHold = true, CapitalReviewRequired = true, HighestClosingValue = 11000 };
    private static TradingApprovedSettings Settings => new() { BindingId = Binding, PermissionId = Permission,
        StrategyId = Strategy, AssignmentId = Strategy, SourceId = Strategy, BaselineId = Strategy,
        ImplementationIdentity = "synthetic-build", ScopeCode = "synthetic-test", ApprovedUtc = Now,
        PurchaseCeiling = 2000, SellAllowed = true, ConfigurationJson = "{\"maximumHoldings\":20}" };
    private static TradingPositionCurrent Position(int id = 1) => new() { BindingId = Binding, PositionId = Id(id),
        SecurityId = "TEST-" + id, Symbol = "TEST" + id, IsOpen = true, Quantity = 10, CostBasis = 100,
        FirstEntryUtc = Now, LastEntryUtc = Now, EntryCount = 1, ProtectionOwner = "synthetic",
        ProtectionStage = "Trailing", ProtectionFloor = 9, HighestClose = 11 };
    private static TradingOrder Order(int id = 1) => new() { BindingId = Binding, RequestId = Id(id + 1000),
        DecisionId = Id(id + 2_000_000), PermissionId = Permission, PositionId = Id(id), SecurityId = "TEST-" + id,
        Symbol = "TEST" + id, Side = "BUY", Quantity = 10, ReferencePrice = 10, CreatedUtc = Now };
    private static Guid Id(int value) { var bytes = new byte[16]; BitConverter.GetBytes(value).CopyTo(bytes, 0); bytes[15] = 7; return new(bytes); }
    private static TradingStateWrite Initial() => new(Guid.NewGuid(), "Initialize", Now, -1, Account) { Settings = [Settings] };
    private static TradingStateWrite Change(TradingCurrentState state, string kind = "Change") =>
        new(Guid.NewGuid(), kind, Now.AddSeconds(state.Account.Version + 1), state.Account.Version, state.Account);
    private static async Task<(Database Db, TradingCurrentState State)> Started()
    {
        var db = new Database();
        await db.Store.SaveAsync(Initial());
        return (db, (await db.Store.ReadAsync(Binding))!);
    }

    [Fact]
    public async Task SavesCurrentRowsAndPreservesProtectionAndStopsAcrossNewReader()
    {
        var db = new Database();
        var p = Position() with { ExitDecisionId = Guid.NewGuid(), ExitReason = "ProtectionFloor", ExitTriggeredUtc = Now };
        await db.Store.SaveAsync(Initial() with { Positions = [p], Orders = [Order()] });
        var current = (await db.Store.ReadAsync(Binding))!;
        Assert.True(current.Account.NewBuysStopped);
        Assert.True(current.Account.DailyLossHold);
        Assert.True(current.Account.CapitalReviewRequired);
        Assert.Equal(11000m, current.Account.HighestClosingValue);
        Assert.Equal(p.ExitDecisionId, Assert.Single(current.Positions).ExitDecisionId);
        Assert.Equal(9m, current.Positions[0].ProtectionFloor);
        Assert.Equal(Permission, current.Settings.PermissionId);
        Assert.Equal(Order().RequestId, Assert.Single(current.Orders).RequestId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InterruptedSaveLeavesNoPartialState(int failedWrite)
    {
        var (db, state) = await Started();
        string before = JsonSerializer.Serialize(state);
        db.FailAfterWrites = failedWrite;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with
        { Account = state.Account with { ObservedCash = 123 }, Positions = [Position()] }));
        db.FailAfterWrites = 0;
        Assert.Equal(before, JsonSerializer.Serialize(await db.Store.ReadAsync(Binding)));
    }

    [Fact]
    public async Task LostCommitReplyRecoversSameOperationWithoutRepeatingWrites()
    {
        var (db, state) = await Started();
        var write = Change(state) with { Positions = [Position()], Orders = [Order()] };
        db.LoseCommitReply = true;
        await Assert.ThrowsAsync<TimeoutException>(() => db.Store.SaveAsync(write));
        int count = db.CommittedWrites;
        var result = await db.Store.SaveAsync(write);
        Assert.True(result.AlreadyCommitted);
        Assert.Equal(0, result.WrittenRows);
        Assert.Equal(count, db.CommittedWrites);
        Assert.Single((await db.Store.ReadAsync(Binding))!.Orders);
    }

    [Fact]
    public async Task ReusedOperationIdentityWithDifferentPayloadIsRejected()
    {
        var (db, state) = await Started();
        var write = Change(state) with { Positions = [Position()] };
        await db.Store.SaveAsync(write);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(write with { Positions = [Position() with { Quantity = 11 }] }));
    }

    [Fact]
    public async Task StaleRetryCannotReapplyAfterBothMarkersHaveRotated()
    {
        var (db, state) = await Started();
        var old = Change(state) with { Positions = [Position()] };
        await db.Store.SaveAsync(old);
        for (int i = 0; i < 3; i++)
        {
            state = (await db.Store.ReadAsync(Binding))!;
            await db.Store.SaveAsync(Change(state) with { Account = state.Account with { ObservedCash = i } });
        }
        int count = db.CommittedWrites;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(old));
        Assert.Equal(count, db.CommittedWrites);
    }

    [Fact]
    public async Task TwoWritersCannotCommitAgainstTheSameVersion()
    {
        var (db, state) = await Started();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt(int cash)
        {
            await start.Task;
            try { await db.Store.SaveAsync(Change(state) with { Account = state.Account with { ObservedCash = cash } }); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var attempts = new[] { Attempt(1), Attempt(2) };
        start.SetResult();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results.Where(x => x));
        Assert.Equal(1, (await db.Store.ReadAsync(Binding))!.Account.Version);
    }

    [Fact]
    public async Task DuplicateDecisionCannotUseANewRequestIdentity()
    {
        var (db, state) = await Started();
        var original = Order();
        await db.Store.SaveAsync(Change(state) with { Orders = [original] });
        state = (await db.Store.ReadAsync(Binding))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with
        { Orders = [original with { RequestId = Guid.NewGuid() }] }));
        Assert.Single((await db.Store.ReadAsync(Binding))!.Orders);
    }

    [Fact]
    public async Task IntentAndApprovalCannotBeRewritten()
    {
        var (db, state) = await Started();
        await db.Store.SaveAsync(Change(state) with { Orders = [Order()] });
        state = (await db.Store.ReadAsync(Binding))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with { Orders = [Order() with { Quantity = 20 }] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with { Settings = [Settings with { PurchaseCeiling = 3000 }] }));
    }

    [Fact]
    public async Task RepeatedCumulativePartialFillDoesNotDuplicateQuantityOrSpend()
    {
        var (db, state) = await Started();
        var order = Order();
        await db.Store.SaveAsync(Change(state) with { Orders = [order] });
        int held = 0;
        decimal spent = 0;
        foreach (int cumulative in new[] { 4, 4, 10, 10 })
        {
            state = (await db.Store.ReadAsync(Binding))!;
            var application = TradingFillApplication.Prepare(state.Orders.Single(), cumulative, cumulative * 10m);
            held += application.QuantityDelta; spent += application.NetValueDelta;
            await db.Store.SaveAsync(Change(state, "FillAccounting") with
            {
                Positions = [Position() with { Quantity = held, CostBasis = spent }], Orders = [application.Order]
            });
        }
        state = (await db.Store.ReadAsync(Binding))!;
        Assert.Equal(10, state.Positions.Single().Quantity);
        Assert.Equal(100m, state.Account.CommittedPurchaseCost);
        Assert.Equal(10, state.Orders.Single().AppliedFilledQuantity);
        Assert.Throws<InvalidOperationException>(() => TradingFillApplication.Prepare(state.Orders.Single(), 9, 90));
    }

    [Fact]
    public async Task OnlyFullyAccountedTerminalOrdersLeaveActiveSet()
    {
        var (db, state) = await Started();
        var order = Order() with { Status = "Filled", LatestFilledQuantity = 10, LatestNetValue = 100 };
        await db.Store.SaveAsync(Change(state) with { Orders = [order] });
        state = (await db.Store.ReadAsync(Binding))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with { Orders = [order with { NeedsWork = false }] }));
        await db.Store.SaveAsync(Change(state) with { Orders = [order with { AppliedFilledQuantity = 10, AppliedNetValue = 100, NeedsWork = false }] });
        Assert.Empty((await db.Store.ReadAsync(Binding))!.Orders);
        Assert.Contains(db.Rows.Values, r => r is TradingOrder o && o.RequestId == order.RequestId);
    }

    [Fact]
    public async Task TenThousandUpdatesReuseRowsAndTwoMarkers()
    {
        var (db, state) = await Started();
        await db.Store.SaveAsync(Change(state) with { Positions = [Position()] });
        state = (await db.Store.ReadAsync(Binding))!;
        int rows = db.Rows.Count;
        for (int i = 1; i <= 10000; i++)
        {
            var position = state.Positions[0] with { ProtectionFloor = 9m + i / 100000m };
            await db.Store.SaveAsync(Change(state) with { Positions = [position] });
            state = (await db.Store.ReadAsync(Binding))!;
        }
        Assert.Equal(rows, db.Rows.Count);
        Assert.Equal(2, state.Checkpoints.Length);
        Assert.True(TradingStorageValidation.PayloadBytes(state) < 8192);
        output.WriteLine($"10,000 updates: {db.Rows.Count} rows, {state.Checkpoints.Length} markers, {TradingStorageValidation.PayloadBytes(state)} current JSON bytes.");
    }

    [Fact]
    public async Task NoOpDoesNotCreateCheckpointOrRewriteRows()
    {
        var (db, state) = await Started();
        int count = db.CommittedWrites;
        for (int i = 0; i < 100; i++) Assert.Equal(0, (await db.Store.SaveAsync(Change(state))).WrittenRows);
        Assert.Equal(count, db.CommittedWrites);
        Assert.Equal(0, (await db.Store.ReadAsync(Binding))!.Account.Version);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(100000)]
    [InlineData(1000000)]
    public async Task CompletedHistoryDoesNotEnterCurrentRead(int historyCount)
    {
        var (db, state) = await Started();
        await db.Store.SaveAsync(Change(state) with { Positions = [Position()], Orders = [Order()] });
        string before = JsonSerializer.Serialize(await db.Store.ReadAsync(Binding));
        var template = Order() with { Status = "Rejected", NeedsWork = false };
        for (int i = 0; i < historyCount; i++)
            db.Seed(template with { RequestId = Id(i + 10_000_000), DecisionId = Id(i + 20_000_000) });
        db.ReadRows = db.ReadCalls = 0;
        var after = (await db.Store.ReadAsync(Binding))!;
        Assert.Equal(before, JsonSerializer.Serialize(after));
        Assert.Equal(6, db.ReadRows); // account/settings/position/order/two checkpoints only
        Assert.Equal(4, db.ReadCalls); // four exact keyed reads; active sets use their own index
        output.WriteLine($"{historyCount:N0} archived receipts: {db.ReadRows} active rows returned, {TradingStorageValidation.PayloadBytes(after)} current JSON bytes. Test-double result, not a SQL timing claim.");
    }

    private static TradingStateWrite BenchmarkBook() => Initial() with
        {
            Positions = Enumerable.Range(1, 20).Select(i => Position(i) with { ProtectionJson = "{\"highestClose\":11,\"tightestGap\":0.02,\"exitTriggered\":false}" }).ToImmutableArray(),
            Symbols = Enumerable.Range(1, 100).Select(i => new TradingSymbolCurrent { BindingId = Binding,
                Symbol = "TEST" + i, IsRelevant = true, StrategyId = Strategy, SessionId = Strategy, TradingDate = Now.Date,
                ProgressJson = "{\"state\":\"Watching\",\"strong\":1,\"weakening\":0}" }).ToImmutableArray(),
            Orders = Enumerable.Range(1, 10).Select(i => Order(i)).ToImmutableArray(),
            Restrictions = Enumerable.Range(1, 20).Select(i => new TradingRestrictionCurrent { BindingId = Binding,
                RestrictionId = Id(i), ScopeKey = "TEST" + i, ActionCode = "Buy", ReasonCode = "Synthetic",
                Explanation = "Synthetic fixture restriction", LastCheckedUtc = Now }).ToImmutableArray()
        };

    [Fact]
    public async Task BenchmarkBookFitsPayloadBudget()
    {
        var db = new Database();
        var initial = BenchmarkBook();
        await db.Store.SaveAsync(initial);
        var state = (await db.Store.ReadAsync(Binding))!;
        await db.Store.SaveAsync(Change(state) with { Account = state.Account with { ObservedCash = 10000 } });
        state = (await db.Store.ReadAsync(Binding))!;
        int bytes = TradingStorageValidation.PayloadBytes(state);
        Assert.True(bytes <= TradingStorageValidation.BenchmarkPayloadBudget, $"Payload is {bytes} bytes.");
        Assert.Equal(154, db.Rows.Count);
        output.WriteLine($"20 holdings/100 symbols/10 orders/20 restrictions: {db.Rows.Count} rows; {bytes:N0} UTF-8 JSON bytes; budget {TradingStorageValidation.BenchmarkPayloadBudget:N0}. Not physical SQL size or broker payload.");
        foreach (var group in db.Rows.Values.GroupBy(r => r.GetType()))
        {
            int budget = group.Key == typeof(TradingAccountCurrent) ? 8192 : group.Key == typeof(TradingApprovedSettings) ? 16384 :
                group.Key == typeof(TradingPositionCurrent) ? 4096 : group.Key == typeof(TradingSymbolCurrent) || group.Key == typeof(TradingOrder) ? 2048 :
                group.Key == typeof(TradingRestrictionCurrent) ? 1024 : 512;
            // Serialize concrete rows, not the base record, so all contract fields are measured.
            int[] sizes = group.Select(r => JsonSerializer.SerializeToUtf8Bytes(r, r.GetType()).Length).ToArray();
            Assert.True(sizes.Max() <= budget, $"{group.Key.Name} fixture row exceeds its preliminary payload budget.");
            output.WriteLine($"{group.Key.Name}: {sizes.Length} rows; {sizes.Sum()} row JSON bytes; maximum row {sizes.Max()} bytes (budget {budget}).");
        }
    }

    [Theory]
    [InlineData("{\"orders\":[1,2]}")]
    [InlineData("{\"previous\":{\"state\":1}}")]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("[]")]
    public void RejectsNestedHistoryAndDuplicateConfigurationKeys(string json) =>
        Assert.ThrowsAny<Exception>(() => TradingStorageValidation.ValidateFlatJson(json));

    [Fact]
    public async Task InvalidScopeSizePrecisionAndTimeCannotWrite()
    {
        var (db, state) = await Started();
        int count = db.CommittedWrites;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with { Positions = [Position() with { BindingId = Guid.NewGuid() }] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with { Positions = [Position() with { Symbol = new string('X', 21) }] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with { Positions = [Position() with { CostBasis = 1.0000001m }] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.SaveAsync(Change(state) with { Positions = [Position() with { FirstEntryUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) }] }));
        Assert.Equal(count, db.CommittedWrites);
    }

    [Fact]
    public async Task CancellationCannotCommit()
    {
        var (db, state) = await Started();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int count = db.CommittedWrites;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Store.SaveAsync(Change(state), cancellation.Token));
        Assert.Equal(count, db.CommittedWrites);
    }

    [Fact]
    public async Task MissingOrMismatchedCurrentMarkerFailsRead()
    {
        var (db, state) = await Started();
        db.Rows.Remove((typeof(TradingCheckpoint), Binding, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.ReadAsync(Binding));
    }
}
