#nullable enable
using Core.Trader.Storage;
using Core.Db;
using Dapper;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class TradingCompactStorageTests
{
    private static ManualSellRecord ManualSale(TradingPositionCurrent position) => new(
        new(Guid.NewGuid(), "identity", "acct", position.SecurityId, "Fixture", position.Symbol, "XTSE",
            1, Now, JournalQuote, Now), WealthsimpleSubmissionStatus.OutcomeUnknown, []);

    private static ManualSellRecord FilledSale(ManualSellRecord sale, DateTime at)
    {
        var zero = new WealthsimpleMoney(0, "CAD");
        var value = new WealthsimpleMoney(10, "CAD");
        return sale with { InventoryVerifiedAt = at, Observations = [new("FILLED", 1, 1, value, value, value,
            zero, zero, at, at, null, "REGULAR", at)] };
    }

    private static void CheckSale(ManualBuyFileJournal journal, TradingPositionCurrent position) =>
        journal.RequireClearForRestartSell("identity", "acct", position.SecurityId, position.PositionId, position.FirstEntryUtc, Id(98765));

    [Fact]
    public Task ConfirmedOpeningAndClosingFillsUpdateTheJournalWithoutArchiveReplay() => ExerciseHoldingCycle(new Database().Store);

    [HoldingLocalDbFact]
    public async Task NativeSqlOpeningClosingAndReentryUpdateTheJournal()
    {
        var db = new RestartRehearsalDatabase(Guid.ParseExact(Environment.GetEnvironmentVariable("TRADERVI_HOLDING_REHEARSAL")!, "N"));
        await using var connection = await db.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT dbo.BrokerAccountImportBinding VALUES (@Binding,N'TFSA',REPLICATE('A',64),REPLICATE('B',64),SYSUTCDATETIME(),N'Synthetic rehearsal');
            """, new { Binding });
        await ExerciseHoldingCycle(new SqlTradingStorageDatabase(db.OpenAsync).CreateStore());
    }

    private sealed class HoldingLocalDbFactAttribute : FactAttribute
    {
        public HoldingLocalDbFactAttribute()
        {
            if (!Guid.TryParseExact(Environment.GetEnvironmentVariable("TRADERVI_HOLDING_REHEARSAL"), "N", out _))
                Skip = "Requires a fresh marked rehearsal LocalDB.";
        }
    }

    private static async Task ExerciseHoldingCycle(TradingCurrentStore store)
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        var protector = new ManualMarketSellWorkflowTests.Protector();
        using var journal = new ManualBuyFileJournal(folder.Path, protector);
        journal.PrepareRestartSellScopes("identity", "acct");
        // A full historical scan after preparation would fail on this unrelated file.
        File.WriteAllBytes(Path.Combine(folder.Path, Guid.NewGuid().ToString("N") + ".sell"), [1]);
        var adapter = new RestartOrderJournal(journal, Binding, "identity", "acct");
        await store.SaveAsync(Initial() with { Account = Account with { ExecutionEnabled = true }, Orders = [InterruptedOrder] });
        var broker = new SimulatedAccountBroker(store) { Status = _ => Cancellation, AccountBook = () => BrokerBook(RecoveryAt, (Position(), 4)) };
        var opened = await broker.Restart(adapter).RunAsync(Binding);
        var first = Assert.Single(opened.Current.Positions);
        Assert.Equal(4, first.Quantity);
        CheckSale(journal, first);
        var watchPath = Assert.Single(Directory.GetFiles(folder.Path, "*.scope"));
        var before = File.ReadAllBytes(watchPath);
        await broker.Restart(adapter).RunAsync(Binding);
        Assert.Equal(before, File.ReadAllBytes(watchPath));

        await store.SaveAsync(Change(opened.Current) with { Positions = [first with { ProtectionOwner = Strategy.ToString("D"),
            ExitDecisionId = Guid.NewGuid(), ExitTriggeredUtc = RecoveryAt.UtcDateTime, ExitReason = "FloorTriggered" }] });
        broker.Status = _ => SellFact(broker.Sent.Single(), "FILLED", 4);
        broker.AccountBook = () => BrokerBook(RecoveryAt);
        var closed = await broker.Restart(adapter).RunAsync(Binding);
        Assert.Empty(closed.Current.Positions);
        Assert.Single(broker.Sent);
        Assert.Throws<InvalidOperationException>(() => CheckSale(journal, first));

        var nextBuy = InterruptedOrder with { RequestId = Guid.NewGuid(), DecisionId = Guid.NewGuid() };
        await store.SaveAsync(Change(closed.Current) with { Orders = [nextBuy] });
        broker.Status = id => Cancellation with { ExternalId = id, LastFilledAt = RecoveryAt };
        broker.AccountBook = () => BrokerBook(RecoveryAt, (Position(), 4));
        var reopened = Assert.Single((await broker.Restart(adapter).RunAsync(Binding)).Current.Positions);
        Assert.NotEqual(first.PositionId, reopened.PositionId);
        Assert.True(reopened.FirstEntryUtc > first.FirstEntryUtc);
        CheckSale(journal, reopened);
        Assert.Throws<InvalidOperationException>(() => CheckSale(journal, first));
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("prior fill", false)]
    [InlineData("overlapping fill", true)]
    [InlineData("rejected", false)]
    public async Task UnwatchedSaleIsCheckedAgainstTheNewEntryBoundary(string outcome, bool blocks)
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        var protector = new ManualMarketSellWorkflowTests.Protector();
        var entry = Now.AddHours(2);
        var p = Position() with { FirstEntryUtc = entry, LastEntryUtc = entry };
        var sale = ManualSale(p);
        using (var writer = new ManualBuyFileJournal(folder.Path, protector))
        {
            writer.PrepareRestartSellScopes("identity", "acct");
            writer.SaveSell(sale);
            if (outcome != "pending")
                writer.SaveSell(outcome == "rejected" ? sale with { Submission = WealthsimpleSubmissionStatus.Rejected } :
                    FilledSale(sale, outcome == "prior fill" ? entry.AddTicks(-1) : entry));
        }
        using var journal = new ManualBuyFileJournal(folder.Path, protector);
        var watched = new Database().Store.WithHoldingObserver(new RestartOrderJournal(journal, Binding, "identity", "acct").AlignHoldings);
        await watched.SaveAsync(Initial() with { Positions = [p, Position(2)] });
        if (blocks) Assert.Throws<InvalidOperationException>(() => CheckSale(journal, p)); else CheckSale(journal, p);
        CheckSale(journal, Position(2));
        if (outcome == "pending")
        {
            journal.SaveSell(sale with { Submission = WealthsimpleSubmissionStatus.Rejected });
            CheckSale(journal, p);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JournalRepairUsesOnlyCommittedPositionsAfterLostReplyOrRollback(bool committed)
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        var protector = new ManualMarketSellWorkflowTests.Protector();
        var db = new Database();
        var p = Position();
        using (var journal = new ManualBuyFileJournal(folder.Path, protector))
        {
            journal.PrepareRestartSellScopes("identity", "acct");
            var store = db.Store.WithHoldingObserver(new RestartOrderJournal(journal, Binding, "identity", "acct").AlignHoldings);
            await store.SaveAsync(Initial());
            var state = (await store.ReadAsync(Binding))!;
            db.LoseCommitReply = committed;
            db.FailAfterWrites = committed ? 0 : 1;
            var write = Change(state) with { Positions = [p] };
            if (committed) await Assert.ThrowsAsync<TimeoutException>(() => store.SaveAsync(write));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(write));
            Assert.Throws<InvalidOperationException>(() => CheckSale(journal, p));
        }
        using var copy = new ManualBuyFileJournal(folder.Path, protector);
        var restart = db.Store.WithHoldingObserver(new RestartOrderJournal(copy, Binding, "identity", "acct").AlignHoldings);
        var current = (await restart.ReadAsync(Binding))!;
        Assert.Equal(committed ? 1 : 0, current.Positions.Length);
        if (committed) CheckSale(copy, p); else Assert.Throws<InvalidOperationException>(() => CheckSale(copy, p));
    }

    [Fact]
    public void InterruptedManualSaleWhileFlatKeepsItsPointerForTheNextOpening()
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        using var journal = new ManualBuyFileJournal(folder.Path, new ManualMarketSellWorkflowTests.Protector());
        journal.PrepareRestartSellScopes("identity", "acct");
        var p = Position();
        var sale = ManualSale(p);
        // Prevent publication of the receipt after its catalog pointer has been flushed.
        var pending = Path.Combine(folder.Path, sale.Intent.RequestId.ToString("N") + ".sell.pending");
        Directory.CreateDirectory(pending);
        var error = Record.Exception(() => journal.SaveSell(sale));
        Assert.True(error is IOException or UnauthorizedAccessException);
        journal.AlignRestartHoldings("identity", "acct", [new(p.SecurityId, p.PositionId, p.FirstEntryUtc)]);
        Assert.Throws<InvalidOperationException>(() => CheckSale(journal, p));
        Directory.Delete(pending);
        journal.SaveSell(sale with { Submission = WealthsimpleSubmissionStatus.Rejected });
        CheckSale(journal, p);
    }

    [Fact]
    public async Task OlderPositionReadCannotPublishOverANewerClose()
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        using var journal = new ManualBuyFileJournal(folder.Path, new ManualMarketSellWorkflowTests.Protector());
        journal.PrepareRestartSellScopes("identity", "acct");
        var adapter = new RestartOrderJournal(journal, Binding, "identity", "acct");
        var db = new Database();
        var p = Position();
        var store = db.Store.WithHoldingObserver(adapter.AlignHoldings);
        await store.SaveAsync(Initial() with { Positions = [p] });
        var state = (await store.ReadAsync(Binding))!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = db.Store.WithHoldingObserver(snapshot =>
        {
            entered.SetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            adapter.AlignHoldings(snapshot);
        });
        var olderRead = Task.Run(() => slow.ReadAsync(Binding));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = store.SaveAsync(Change(state) with { Positions = [p with { IsOpen = false, Quantity = 0, CostBasis = 0 }] });
        try { Assert.False(close.IsCompleted); }
        finally { release.SetResult(); }
        await Task.WhenAll(olderRead, close);
        Assert.Throws<InvalidOperationException>(() => CheckSale(journal, p));
    }

    [Fact]
    public async Task MissingCatalogPreservesConfirmedFillAndRestrictsOnlyAnUncoveredHolding()
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        using var journal = new ManualBuyFileJournal(folder.Path, new ManualMarketSellWorkflowTests.Protector());
        var other = Position(2);
        journal.PrepareRestartSellScopes("identity", "acct", new ManualBuyFileJournal.RestartHolding(other.SecurityId, other.PositionId, other.FirstEntryUtc));
        var path = Assert.Single(Directory.GetFiles(folder.Path, "*.catalog"));
        var savedCatalog = File.ReadAllBytes(path);
        File.Delete(path);
        var db = new Database();
        await db.Store.SaveAsync(Initial() with { Positions = [other], Orders = [InterruptedOrder] });
        var broker = new SimulatedAccountBroker(db) { Status = _ => Cancellation,
            AccountBook = () => BrokerBook(RecoveryAt, (Position(), 4), (other, other.Quantity)) };
        var adapter = new RestartOrderJournal(journal, Binding, "identity", "acct");
        var pass = await broker.Restart(adapter).RunAsync(Binding);
        Assert.Contains(pass.Notices, n => n.Action == "Journal coverage");
        AssertAllowance(pass.Current, 38, 0);
        var opened = pass.Current.Positions.Single(p => p.SecurityId == InterruptedOrder.SecurityId);
        CheckSale(journal, other);
        Assert.Throws<InvalidOperationException>(() => CheckSale(journal, opened));
        File.WriteAllBytes(path, savedCatalog);
        var recovered = await broker.Restart(adapter).RunAsync(Binding);
        Assert.DoesNotContain(recovered.Notices, n => n.Action == "Journal coverage");
        AssertAllowance(recovered.Current, 38, 0);
        CheckSale(journal, opened);
    }

    [Fact]
    public void CompletedSalesOnOneSecurityReuseOneCatalogEntry()
    {
        using var folder = new ManualMarketSellWorkflowTests.TempDirectory();
        var protector = new ManualMarketSellWorkflowTests.Protector();
        using var journal = new ManualBuyFileJournal(folder.Path, protector);
        journal.PrepareRestartSellScopes("identity", "acct");
        var p = Position();
        var watch = Assert.Single(Directory.GetFiles(folder.Path, "*.scope"));
        var before = File.ReadAllBytes(watch);
        for (int i = 0; i < 64; i++)
            journal.SaveSell(FilledSale(ManualSale(p), Now.AddSeconds(i)));
        var path = Assert.Single(Directory.GetFiles(folder.Path, "*.catalog"));
        using var data = System.Text.Json.JsonDocument.Parse(protector.Unprotect(File.ReadAllBytes(path)));
        var evidence = Assert.Single(data.RootElement.GetProperty("Securities").EnumerateObject()).Value;
        Assert.Empty(evidence.GetProperty("Pending").EnumerateArray());
        Assert.Equal(Now.AddSeconds(63), evidence.GetProperty("LastFill").GetDateTimeOffset().UtcDateTime);
        Assert.Equal(before, File.ReadAllBytes(watch));
    }
}
