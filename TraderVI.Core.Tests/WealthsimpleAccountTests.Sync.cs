using System;
using System.Threading.Tasks;
using Core.WSTrade.Accounts;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    [Fact] public async Task AccountRefreshInvokesSynchronizationAndShowsItsResult()
    {
        var source = new Source(); int calls = 0;
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (account, revision, ct) =>
        { Assert.Same(source.Accounts[0], account); Assert.Equal(source.SessionRevision, revision); calls++; return Task.FromResult(SynchronizedRead(account, DateTimeOffset.UtcNow) with { TradeSyncStatus = "Saved broker trades" }); });
        await vm.RefreshAsync(); Assert.Equal(1, calls); Assert.Contains("Saved broker trades", vm.TradeSyncStatus);
    }
    [Fact] public async Task SynchronizationFailureIsVisibleAndDoesNotEraseBrokerHoldings()
    {
        var source = new Source(); bool fail = false;
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (account, _, _) => fail
            ? throw new InvalidOperationException("private broker detail") : Task.FromResult(SynchronizedRead(account, DateTimeOffset.UtcNow)));
        await vm.RefreshAsync(); var prior = vm.Snapshot;
        fail = true; await vm.RefreshAsync(); Assert.Contains("could not complete", vm.TradeSyncStatus);
        Assert.DoesNotContain("private", vm.TradeSyncStatus); Assert.NotNull(prior); Assert.Same(prior, vm.Snapshot); Assert.Null(vm.PendingOrders);
    }
    [Fact] public async Task OldLoginSynchronizationResultIsDiscarded()
    {
        var source = new Source(); var pending = new TaskCompletionSource<BrokerAccountSynchronization>();
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (_, _, _) => pending.Task);
        var refresh = vm.RefreshAsync(); source.Disconnect(); pending.SetResult(SynchronizedRead(source.Accounts[0], DateTimeOffset.UtcNow) with { TradeSyncStatus = "Saved old account" }); await refresh;
        Assert.DoesNotContain("Saved old account", vm.TradeSyncStatus); Assert.Null(vm.Snapshot);
    }
}
