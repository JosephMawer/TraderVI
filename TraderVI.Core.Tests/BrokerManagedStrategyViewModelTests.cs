#nullable enable
using Core.Trader.Promotion;
using System;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerManagedStrategyViewModelTests
{
    private sealed class Clock(BrokerManagedSessionTests.Fixture fixture) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => fixture.Now; }
    private static BrokerManagedSessionControls Controls(BrokerManagedSessionTests.Fixture f) => new(f.Store, new Clock(f));

    [Fact]
    public async Task VerifiedStartupRestoresSavedOngoingSelectionAndStoppedModeWithoutExecution()
    {
        var f = new BrokerManagedSessionTests.Fixture(); await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId); f.Store.Restart();
        using var model = new BrokerManagedStrategyViewModel(Controls(f), new Clock(f));
        Assert.False(model.CanResumeBuying);
        await model.RestoreAsync(f.Binding.BindingId);
        Assert.Equal(f.Strategy.Strategy.Name, model.StrategyName); Assert.Equal("Ongoing strategy selection", model.Duration);
        Assert.True(model.CanResumeBuying); Assert.False(model.CanStopBuying);
        Assert.Contains("New buys stopped", model.BuyingMode); Assert.Contains("execution is not enabled", model.Status);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task StopAndResumePersistOnlyTheDisplayedAssignmentsMode()
    {
        var f = new BrokerManagedSessionTests.Fixture(); var approved = await f.Workflow().ApproveAsync(f.Approval());
        using var model = new BrokerManagedStrategyViewModel(Controls(f), new Clock(f));
        await model.RestoreAsync(f.Binding.BindingId); Assert.True(model.CanStopBuying);
        await model.StopBuyingAsync(); Assert.True(f.Store.State!.NewBuysStopped); Assert.True(model.CanResumeBuying);
        await model.ResumeBuyingAsync(); Assert.False(f.Store.State.NewBuysStopped); Assert.True(model.CanStopBuying);
        Assert.Equal(approved.Approval, f.Store.State.Approval); Assert.Equal(approved.Protection, f.Store.State.Protection);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task MissingApprovalDoesNotCreateOneOrEnableControls()
    {
        var f = new BrokerManagedSessionTests.Fixture(); using var model = new BrokerManagedStrategyViewModel(Controls(f));
        await model.RestoreAsync(f.Binding.BindingId); await model.ResumeBuyingAsync(); await model.StopBuyingAsync();
        Assert.False(model.CanStopBuying); Assert.False(model.CanResumeBuying); Assert.Null(f.Store.State);
        Assert.Contains("No strategy has been approved", model.Status);
    }

    [Fact]
    public async Task LostSaveAcknowledgementRequiresReadbackAndRecoversTheCommittedMode()
    {
        var f = new BrokerManagedSessionTests.Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        using var model = new BrokerManagedStrategyViewModel(Controls(f), new Clock(f)); await model.RestoreAsync(f.Binding.BindingId);
        f.Store.LoseResponseAtEvent = "OperatorStoppedNewBuys";
        await model.StopBuyingAsync(); var revision = f.Store.State!.RevisionId;
        Assert.False(model.CanStopBuying); Assert.False(model.CanResumeBuying); Assert.Contains("could not be confirmed", model.Status);
        await model.StopBuyingAsync(); Assert.Equal(revision, f.Store.State.RevisionId);
        f.Store.Restart(); await model.RestoreAsync(f.Binding.BindingId);
        Assert.True(model.CanResumeBuying); Assert.Equal(revision, f.Store.State!.RevisionId);
    }

    [Fact]
    public async Task StaleAccountOrFailedRefreshDisablesModeChangesWithoutErasingSelection()
    {
        var f = new BrokerManagedSessionTests.Fixture(); await f.Workflow().ApproveAsync(f.Approval());
        using var model = new BrokerManagedStrategyViewModel(Controls(f), new Clock(f)); await model.RestoreAsync(f.Binding.BindingId);
        f.Now = f.Now.AddMinutes(6); await model.StopBuyingAsync(); Assert.False(f.Store.State!.NewBuysStopped);
        model.Invalidate(); Assert.Equal(f.Strategy.Strategy.Name, model.StrategyName); Assert.False(model.CanStopBuying);
        await model.RestoreAsync(f.Binding.BindingId); Assert.True(model.CanStopBuying);
        model.Invalidate(true); Assert.Equal("No approved strategy loaded", model.StrategyName); Assert.False(model.CanStopBuying);
    }

    [Fact]
    public async Task ChangedAssignmentOrWrongBindingCannotReceiveAStaleControl()
    {
        var f = new BrokerManagedSessionTests.Fixture(); var approved = await f.Workflow().ApproveAsync(f.Approval());
        var controls = Controls(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controls.SetBuyingStoppedAsync(f.Binding.BindingId, Guid.NewGuid(), true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controls.SetBuyingStoppedAsync(Guid.NewGuid(), approved.SessionId, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controls.ReadAsync(Guid.NewGuid()));
        Assert.Equal(approved, f.Store.State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LogoutOrShutdownDiscardsALateRestoration(bool shutdown)
    {
        var f = new BrokerManagedSessionTests.Fixture(); var approved = await f.Workflow().ApproveAsync(f.Approval());
        var deferred = new TaskCompletionSource<BrokerManagedSession?>();
        using var model = new BrokerManagedStrategyViewModel(new FakeControls { Read = (_, _) => deferred.Task });
        var read = model.RestoreAsync(f.Binding.BindingId);
        if (shutdown) model.Dispose(); else model.Invalidate(true);
        deferred.SetResult(approved); await read;
        Assert.Equal("No approved strategy loaded", model.StrategyName); Assert.False(model.CanStopBuying);
    }

    [Fact]
    public async Task ExternalCancellationAndReadFailureNeverEnableControls()
    {
        var f = new BrokerManagedSessionTests.Fixture(); var approved = await f.Workflow().ApproveAsync(f.Approval());
        var deferred = new TaskCompletionSource<BrokerManagedSession?>();
        var controls = new FakeControls { Read = (_, _) => deferred.Task };
        using var model = new BrokerManagedStrategyViewModel(controls); using var cancellation = new CancellationTokenSource();
        var read = model.RestoreAsync(f.Binding.BindingId, cancellation.Token); cancellation.Cancel();
        deferred.SetResult(approved); await read; Assert.False(model.CanStopBuying);
        controls.Read = (_, _) => throw new InvalidOperationException("synthetic store failure");
        await model.RestoreAsync(f.Binding.BindingId); Assert.False(model.CanStopBuying);
        Assert.Contains("could not be verified", model.Status);
    }

    private sealed class FakeControls : IBrokerManagedSessionControls
    {
        internal Func<Guid, CancellationToken, Task<BrokerManagedSession?>> Read = (_, _) => Task.FromResult<BrokerManagedSession?>(null);
        public Task<BrokerManagedSession?> ReadAsync(Guid bindingId, CancellationToken ct) => Read(bindingId, ct);
        public Task<BrokerManagedSession> SetBuyingStoppedAsync(Guid bindingId, Guid expectedSessionId, bool stopped, CancellationToken ct) =>
            throw new InvalidOperationException("No control write is expected in this test.");
    }
}
