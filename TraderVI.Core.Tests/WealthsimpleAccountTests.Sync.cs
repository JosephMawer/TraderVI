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
    [Fact] public async Task SynchronizationFailureRefreshesHoldingsWithoutVerifyingTradeHistory()
    {
        var source = new Source(); bool fail = false;
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (account, _, _) => fail
            ? throw new InvalidOperationException("private broker detail") : Task.FromResult(SynchronizedRead(account, DateTimeOffset.UtcNow)));
        await vm.RefreshAsync(); var prior = vm.Snapshot;
        Assert.Empty(prior.Holdings);
        var fresh = Snapshot(source.Accounts[0]) with { Holdings = [new("TEST", "Test holding", "EQUITY", 2, null, null, null)] };
        source.Read = _ => Task.FromResult(new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success, fresh));
        source.ReadOrders = _ => throw new Exception("A display read cannot verify pending orders.");
        fail = true; await vm.RefreshAsync(); Assert.Contains("could not complete", vm.TradeSyncStatus);
        Assert.DoesNotContain("private", vm.TradeSyncStatus); Assert.Same(fresh, vm.Snapshot); Assert.Null(vm.PendingOrders);
        Assert.Equal("TEST", Assert.Single(vm.Holdings).Symbol);
        Assert.Contains("Holdings and balances updated", vm.Status);
    }
    [Fact] public async Task DisplayFallbackDiscardsLateSnapshotAfterLogout()
    {
        var source = new Source();
        var read = new TaskCompletionSource<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>>();
        source.Read = _ => read.Task;
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (_, _, _) => throw new InvalidOperationException());
        var refreshing = vm.RefreshAsync();
        source.Disconnect();
        read.SetResult(new(WealthsimpleReadStatus.Success, Snapshot(source.Accounts[0])));
        await refreshing;
        Assert.Null(vm.Snapshot); Assert.Null(vm.PendingOrders);
    }
    [Fact] public async Task OldLoginSynchronizationResultIsDiscarded()
    {
        var source = new Source(); var pending = new TaskCompletionSource<BrokerAccountSynchronization>();
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (_, _, _) => pending.Task);
        var refresh = vm.RefreshAsync(); source.Disconnect(); pending.SetResult(SynchronizedRead(source.Accounts[0], DateTimeOffset.UtcNow) with { TradeSyncStatus = "Saved old account" }); await refresh;
        Assert.DoesNotContain("Saved old account", vm.TradeSyncStatus); Assert.Null(vm.Snapshot);
    }
}
