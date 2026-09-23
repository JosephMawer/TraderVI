using Core.WSTrade.Accounts;
using Core.Trader.Promotion;
using System;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class WealthsimpleAccountTests
{
    private static BrokerAccountSynchronization SynchronizedRead(WealthsimpleTfsaAccount account, DateTimeOffset at) => new(
        new(new("fixture-identity", account.Id, at, at, 100, 100, 100, [], [], [], []),
            Snapshot(account) with { RetrievedAt = at }, new(account, at, [])), null, ["First link requires review."]);

    [Fact]
    public async Task SavedStrategyRestoresOnlyAfterLinkedRefreshAndClearsOnLogout()
    {
        var source = new Source(); var clock = new Clock();
        var f = new BrokerManagedSessionTests.Fixture(); await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        var controls = new BrokerManagedStrategyViewModel(new BrokerManagedSessionControls(f.Store), clock);
        var deferred = new TaskCompletionSource<BrokerAccountSynchronization>();
        using var vm = new WealthsimpleAccountViewModel(source, clock, (_, _, _) => deferred.Task, managedStrategy: controls);
        var read = vm.RefreshAsync(); Assert.False(controls.CanResumeBuying);
        deferred.SetResult(SynchronizedRead(source.Accounts[0], clock.Now) with { Saved = f.Project().Import, Blocks = [] });
        await read; Assert.True(controls.CanResumeBuying); Assert.Equal(f.Strategy.Strategy.Name, controls.StrategyName);
        Assert.Equal(0, f.Transport.Sent);
        source.Disconnect(); Assert.False(controls.CanResumeBuying); Assert.Equal("No approved strategy loaded", controls.StrategyName);
    }

    [Fact]
    public async Task UnlinkedOrFailedAccountRefreshCannotRestoreStrategyControls()
    {
        var source = new Source(); var clock = new Clock();
        var f = new BrokerManagedSessionTests.Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        var controls = new BrokerManagedStrategyViewModel(new BrokerManagedSessionControls(f.Store), clock);
        bool failed = false;
        using var vm = new WealthsimpleAccountViewModel(source, clock, (a, _, _) => failed
            ? throw new InvalidOperationException("synthetic failed read") : Task.FromResult(SynchronizedRead(a, clock.Now)), managedStrategy: controls);
        await vm.RefreshAsync(); Assert.False(controls.CanStopBuying);
        failed = true; await vm.RefreshAsync(); Assert.False(controls.CanStopBuying);
        Assert.Equal("No approved strategy loaded", controls.StrategyName); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task BackgroundAccountPollRunsEveryFiveMinutesAndUsesSharedFactsWithoutExtraDisplayReads()
    {
        var source = new Source(); var clock = new Clock(); int reads = 0;
        source.Read = _ => throw new Exception("A separate display snapshot must not be fetched.");
        source.ReadOrders = _ => throw new Exception("A separate pending feed must not be fetched.");
        var read = SynchronizedRead(source.Accounts[0], clock.Now);
        using var vm = new WealthsimpleAccountViewModel(source, clock, (_, _, _) => { reads++; return Task.FromResult(read); });
        await vm.PollAsync(); Assert.Equal(1, reads);
        Assert.Same(read.Read.Snapshot, vm.Snapshot); Assert.Same(read.Read.PendingOrders, vm.PendingOrders);
        Assert.Contains("history needs attention", vm.Status);
        clock.Now += TimeSpan.FromMinutes(4); await vm.PollAsync(); Assert.Equal(1, reads);
        clock.Now += TimeSpan.FromMinutes(1); await vm.PollAsync(); Assert.Equal(2, reads);
        await vm.RefreshAsync(); Assert.Equal(3, reads); // Manual refresh does not wait for the interval.
    }

    [Fact]
    public async Task PollDefersWhileAnOrderIsRunningAndDoesNotDelayTheNextOpportunity()
    {
        var source = new Source(); var clock = new Clock(); int reads = 0; bool busy = true;
        using var vm = new WealthsimpleAccountViewModel(source, clock,
            (a, _, _) => { reads++; return Task.FromResult(SynchronizedRead(a, clock.Now)); }, () => !busy);
        await vm.PollAsync(); Assert.Equal(0, reads);
        busy = false; await vm.PollAsync(); Assert.Equal(1, reads);
    }

    [Fact]
    public async Task FailedPollingBacksOffRetainsPriorSnapshotAndResetsAfterSuccess()
    {
        var source = new Source(); var clock = new Clock(); int reads = 0; bool fail = false;
        using var vm = new WealthsimpleAccountViewModel(source, clock, (a, _, _) =>
        {
            reads++; if (fail) throw new InvalidOperationException("private broker detail");
            return Task.FromResult(SynchronizedRead(a, clock.Now));
        });
        await vm.PollAsync(); var prior = vm.Snapshot;
        fail = true; clock.Now += TimeSpan.FromMinutes(5); await vm.PollAsync();
        Assert.Same(prior, vm.Snapshot); Assert.Null(vm.PendingOrders); Assert.DoesNotContain("private", vm.Status);
        Assert.Contains("retry in 5", vm.Status);
        clock.Now += TimeSpan.FromMinutes(5); await vm.PollAsync(); Assert.Contains("retry in 10", vm.Status);
        clock.Now += TimeSpan.FromMinutes(5); await vm.PollAsync(); Assert.Equal(3, reads);
        clock.Now += TimeSpan.FromMinutes(5); await vm.PollAsync(); Assert.Contains("retry in 20", vm.Status);
        fail = false; await vm.RefreshAsync(); Assert.Equal(5, reads);
        clock.Now += TimeSpan.FromMinutes(5); await vm.PollAsync(); Assert.Equal(6, reads);
    }

    [Fact]
    public async Task ManualRequestsDuringAnInFlightPollCoalesceIntoOneLaterFreshRead()
    {
        var source = new Source(); var clock = new Clock(); int reads = 0;
        var first = new TaskCompletionSource<BrokerAccountSynchronization>();
        var latest = SynchronizedRead(source.Accounts[0], clock.Now.AddSeconds(1));
        using var vm = new WealthsimpleAccountViewModel(source, clock, (_, _, _) => ++reads == 1 ? first.Task : Task.FromResult(latest));
        var poll = vm.PollAsync(); var manual = vm.RefreshAsync(); var postTrade = vm.RefreshAsync();
        await vm.PollAsync(); Assert.Equal(1, reads);
        first.SetResult(SynchronizedRead(source.Accounts[0], clock.Now));
        await Task.WhenAll(poll, manual, postTrade);
        Assert.Equal(2, reads); Assert.Same(latest.Read.Snapshot, vm.Snapshot); Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LogoutOrShutdownCancelsBackgroundReadAndDiscardsLateResult(bool shutdown)
    {
        var source = new Source(); var clock = new Clock(); CancellationToken captured = default;
        var result = new TaskCompletionSource<BrokerAccountSynchronization>();
        using var vm = new WealthsimpleAccountViewModel(source, clock, (_, _, ct) => { captured = ct; return result.Task; });
        var poll = vm.PollAsync();
        if (shutdown) vm.Dispose(); else source.Disconnect();
        Assert.True(captured.IsCancellationRequested);
        result.SetResult(SynchronizedRead(source.Accounts[0], clock.Now)); await poll;
        Assert.Null(vm.Snapshot); Assert.Null(vm.PendingOrders);
        await vm.PollAsync(); Assert.Null(vm.Snapshot);
    }

    [Fact]
    public async Task BackgroundResultForADifferentAccountIsDiscarded()
    {
        var source = new Source(); var clock = new Clock();
        using var vm = new WealthsimpleAccountViewModel(source, clock,
            (_, _, _) => Task.FromResult(SynchronizedRead(new("other", "Other TFSA"), clock.Now)));
        await vm.PollAsync(); Assert.Null(vm.Snapshot); Assert.Null(vm.PendingOrders);
        Assert.Contains("could not complete", vm.Status);
    }
}
