using System;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    [Fact] public async Task AccountRefreshInvokesSynchronizationAndShowsItsResult()
    {
        var source = new Source(); int calls = 0;
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (account, revision, ct) =>
        { Assert.Same(source.Accounts[0], account); Assert.Equal(source.SessionRevision, revision); calls++; return Task.FromResult("Saved broker trades"); });
        await vm.RefreshAsync(); Assert.Equal(1, calls); Assert.Contains("Saved broker trades", vm.TradeSyncStatus);
    }
    [Fact] public async Task SynchronizationFailureIsVisibleAndDoesNotEraseBrokerHoldings()
    {
        using var vm = new WealthsimpleAccountViewModel(new Source(), synchronize: (_, _, _) => throw new InvalidOperationException("conflicting history"));
        await vm.RefreshAsync(); Assert.Contains("conflicting history", vm.TradeSyncStatus); Assert.NotNull(vm.Snapshot);
    }
    [Fact] public async Task OldLoginSynchronizationResultIsDiscarded()
    {
        var source = new Source(); var pending = new TaskCompletionSource<string>();
        using var vm = new WealthsimpleAccountViewModel(source, synchronize: (_, _, _) => pending.Task);
        var refresh = vm.RefreshAsync(); source.Disconnect(); pending.SetResult("Saved old account"); await refresh;
        Assert.DoesNotContain("Saved old account", vm.TradeSyncStatus); Assert.Null(vm.Snapshot);
    }
}
