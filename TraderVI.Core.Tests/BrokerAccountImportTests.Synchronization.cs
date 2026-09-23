#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class BrokerAccountImportTests
{
    private static BrokerAccountRead AccountRead(BrokerAccountReconciliation? observation = null) => new(observation ?? Observation(),
        new(Account, Now, new(300, "CAD"), new(199, "CAD"), new(198, "CAD"), null, null, null, null, null, []),
        new(Account, Now, []));

    [Fact]
    public async Task BackgroundSynchronizationNeverCreatesTheFirstAccountLink()
    {
        var store = new Store(); int reads = 0;
        var sync = new BrokerAccountSynchronizationWorkflow(new(store, new Clock()));
        var result = await sync.RefreshAsync(_ => { reads++; return Task.FromResult(AccountRead()); });
        Assert.Null(result.Saved); Assert.Single(result.Blocks); Assert.Equal(1, reads);
        Assert.Null(store.State.Binding); Assert.Empty(store.Receipts);
    }

    [Fact]
    public async Task LinkedSynchronizationReturnsTheExactFinalReadThatWasSaved()
    {
        var store = new Store { State = Linked(Plan()) }; int reads = 0;
        var first = AccountRead(); var final = AccountRead();
        var sync = new BrokerAccountSynchronizationWorkflow(new(store, new Clock()));
        var result = await sync.RefreshAsync(_ => Task.FromResult(++reads == 1 ? first : final));
        Assert.Equal(2, reads); Assert.Same(final, result.Read);
        Assert.Same(final.Reconciliation, result.Saved!.Plan.Observation);
        Assert.Same(result.Saved, store.State.Latest); Assert.Empty(result.Blocks);
        Assert.Single(result.Saved.Plan.Observation.Orders); Assert.Single(store.Receipts);
    }

    [Theory]
    [InlineData("stale")] [InlineData("conflict")] [InlineData("uncertain")] [InlineData("other-account")]
    public async Task BackgroundSynchronizationPreservesSavedHistoryWhenFactsAreNotUsable(string reason)
    {
        var store = new Store { State = Linked(Plan()) }; var prior = store.State.Latest;
        var observation = reason switch
        {
            "stale" => Observation() with { SnapshotAt = Now.AddMinutes(-3) },
            "conflict" => Observation() with { HistoryConflicts = ["An imported fill conflicts."] },
            "uncertain" => Observation() with { Blocks = ["A saved local order still needs status recovery."] },
            _ => Observation() with { AccountId = "another-account" }
        };
        var result = await new BrokerAccountSynchronizationWorkflow(new(store, new Clock()))
            .RefreshAsync(_ => Task.FromResult(AccountRead(observation)));
        Assert.Null(result.Saved); Assert.NotEmpty(result.Blocks); Assert.Same(prior, store.State.Latest);
        Assert.Empty(store.Receipts);
    }

    [Fact]
    public async Task ChangingFinancialFactsBetweenReadsCannotBeSavedOrPresentedAsSynchronized()
    {
        var store = new Store { State = Linked(Plan()) }; int reads = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BrokerAccountSynchronizationWorkflow(new(store, new Clock()))
            .RefreshAsync(_ => Task.FromResult(AccountRead(++reads == 1 ? Observation() : Observation() with { Cash = 198 }))));
        Assert.Empty(store.Receipts);
    }

    [Fact]
    public async Task CancelledSynchronizationCannotAppendHistory()
    {
        var store = new Store { State = Linked(Plan()) }; using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BrokerAccountSynchronizationWorkflow(new(store, new Clock()))
            .RefreshAsync(_ => { cancellation.Cancel(); return Task.FromResult(AccountRead()); }, cancellation.Token));
        Assert.Empty(store.Receipts);
    }

    [Fact]
    public async Task RestartedSynchronizationRetainsPartialTerminalFillWithoutDuplicatingItsTotals()
    {
        var store = new Store { State = Linked(Plan()) };
        var sell = Sell() with { Status = "CANCELLED", SubmittedQuantity = 10 };
        var read = AccountRead(Observation(6, 60.6m, Buy(), sell));
        for (int restart = 0; restart < 2; restart++)
        {
            var result = await new BrokerAccountSynchronizationWorkflow(new(store, new Clock()))
                .RefreshAsync(_ => Task.FromResult(read));
            Assert.NotNull(result.Saved);
            var orders = result.Saved!.Plan.Observation.Orders;
            Assert.Equal(2, orders.Count); Assert.Equal(4, orders.Single(o => o.Side == "SELL").FilledQuantity);
            Assert.Equal(6, Assert.Single(result.Saved.Plan.Observation.Holdings).BrokerQuantity);
        }
        Assert.Equal(2, store.Receipts.Count);
    }
}
