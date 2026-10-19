#nullable enable
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static ITsxSessionCalendar HostCalendar() => new ReviewedTsxSessionCalendar(new("fixture", "offline",
        new(2026,9,22), new(2026,9,25), [new(2026,9,23), new(2026,9,24)]));
    private static BrokerExecutionHost Host(Fixture f, HostGrants grants,
        Func<BrokerManagedSession, CancellationToken, Task<BrokerManagedSession>>? tick = null,
        string? implementation = null) => new(grants, new BrokerManagedSessionControls(f.Store),
        tick ?? ((saved, ct) => f.Workflow().TickAsync(saved.BindingId, ct)), HostCalendar(),
        implementation ?? f.ImplementationIdentity, new HostClock(f));
    private static Task Enable(BrokerExecutionHost host, Fixture f) => host.EnableAsync(f.Binding.BindingId,
        f.Store.State!.SessionId, f.Strategy.Fingerprint, "offline fixture");

    [Fact]
    public async Task HostOffDoesNotReadAnAssignmentCreateApprovalOrInvokeTheEngine()
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants(); int calls = 0;
        var host = Host(f, grants, (saved, ct) => { calls++; return Task.FromResult(saved); });
        await host.PollAsync(true);
        Assert.Equal("Off", host.Status.State); Assert.Equal(0, calls); Assert.Null(f.Store.State); Assert.Null(grants.Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.EnableAsync(f.Binding.BindingId,
            Guid.NewGuid(), f.Strategy.Fingerprint, "fixture"));
        Assert.Null(grants.Value); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task ExplicitEnableSavesOnlyHostPermissionAndTheFollowingCycleUsesTheApprovedWorkflow()
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants();
        await f.Workflow().ApproveAsync(f.Approval(retain:true, ongoing:true));
        var host = Host(f, grants); await Enable(host, f);
        Assert.True(grants.Value!.Enabled); Assert.Null(f.Store.State!.ActivatedAt); Assert.Equal(0, f.Transport.Sent);
        await host.PollAsync(true);
        Assert.Equal("Running", host.Status.State); Assert.NotNull(f.Store.State.ActivatedAt);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task RestartPreservesOriginalOrderAndNeverResubmitsAnUncertainSale()
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants();
        await f.Workflow().ApproveAsync(f.Approval(ongoing:true));
        var host = Host(f, grants); await Enable(host, f); await host.PollAsync(true);
        var request = Assert.Single(f.Store.State!.Orders).RequestId; Assert.Equal(1, f.Transport.Sent);
        f.Store.Restart(); f.Now = f.Now.AddSeconds(31);
        await Host(f, grants).PollAsync(true);
        Assert.Equal(request, Assert.Single(f.Store.State!.Orders).RequestId); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task HostRestartRetainsStoppedBuyingAndFullHostStopDoesNotChangeTheAssignment()
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants();
        await f.Workflow().ApproveAsync(f.Approval(retain:true, ongoing:true));
        await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        var host = Host(f, grants); await Enable(host, f); await host.PollAsync(true);
        Assert.True(f.Store.State!.NewBuysStopped); Assert.Contains("buys are stopped", host.Status.Message);
        var revision = f.Store.State.Revision;
        await host.DisableAsync(); Assert.False(grants.Value!.Enabled);
        await Host(f, grants).PollAsync(true);
        Assert.Equal(revision, f.Store.State.Revision); Assert.True(f.Store.State.NewBuysStopped);
    }

    [Theory]
    [InlineData("disconnected")][InlineData("before-open")][InlineData("after-close")][InlineData("non-session")]
    public async Task HostWaitsWithoutAnExecutionCycleWhenTheAccountOrSessionIsUnavailable(string reason)
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants(); int calls = 0;
        await f.Workflow().ApproveAsync(f.Approval(retain:true, ongoing:true));
        var host = Host(f, grants, (saved, ct) => { calls++; return Task.FromResult(saved); }); await Enable(host, f);
        var bounds = HostCalendar().GetSessionBounds(new(2026,9,23));
        if (reason == "before-open") f.Now = new DateTimeOffset(bounds.OpenUtc).AddSeconds(-1);
        if (reason == "after-close") f.Now = new DateTimeOffset(bounds.CloseUtc);
        if (reason == "non-session") f.Now = f.Now.AddDays(2);
        await host.PollAsync(reason != "disconnected"); Assert.Equal(0, calls); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("build")][InlineData("assignment")][InlineData("invalidated")][InlineData("expired")][InlineData("paused")]
    public async Task HostNeverTreatsAChangedOrPausedAssignmentAsFreshAuthority(string reason)
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants(); int calls = 0;
        await f.Workflow().ApproveAsync(f.Approval(retain:true));
        var host = Host(f, grants, (saved, ct) => { calls++; return Task.FromResult(saved); }); await Enable(host, f);
        if (reason == "build") host = Host(f, grants, implementation:"changed-build");
        if (reason == "assignment") grants.Value = grants.Value! with { SessionId = Guid.NewGuid() };
        if (reason == "invalidated") f.Store.State = f.Store.State! with { ReviewInvalidated = true };
        if (reason == "paused") await f.Workflow().PauseAsync(f.Binding.BindingId);
        if (reason == "expired") f.Now = f.Now.AddDays(1);
        await host.PollAsync(true); Assert.Equal(0, calls); Assert.Equal(0, f.Transport.Sent);
        Assert.Contains(host.Status.State, new[] { "Blocked", "Paused" });
    }

    [Fact]
    public async Task ConcurrentPollsCoalesceAndStopWaitsForTheExistingCycle()
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants();
        await f.Workflow().ApproveAsync(f.Approval(retain:true, ongoing:true));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        var host = Host(f, grants, async (saved, ct) => { calls++; started.SetResult(); await release.Task; return saved; });
        await Enable(host, f); var first = host.PollAsync(true); await started.Task;
        await host.PollAsync(true); var stopping = host.DisableAsync();
        Assert.False(stopping.IsCompleted); Assert.Equal(1, calls);
        release.SetResult(); await first; await stopping;
        Assert.False(grants.Value!.Enabled); Assert.Equal("Off", host.Status.State);
    }

    [Fact]
    public async Task FailedReadsBackOffWithoutChangingPermissionOrExposingPrivateErrors()
    {
        var f = new Fixture(); UseNormalLive(f); var grants = new HostGrants(); int calls = 0;
        await f.Workflow().ApproveAsync(f.Approval(retain:true, ongoing:true));
        var host = Host(f, grants, (saved, ct) => { calls++; throw new IOException("private fixture detail"); });
        await Enable(host, f); var original = grants.Value;
        await host.PollAsync(true); Assert.Equal(f.Now.AddSeconds(30), host.Status.NextCheckAt);
        f.Now = f.Now.AddSeconds(15); await host.PollAsync(true); Assert.Equal(1, calls);
        f.Now = f.Now.AddSeconds(16); await host.PollAsync(true); Assert.Equal(2, calls);
        Assert.Equal(f.Now.AddSeconds(60), host.Status.NextCheckAt); Assert.Equal(original, grants.Value);
        Assert.DoesNotContain("private", host.Status.Message);
    }

    [Fact]
    public void ProtectedHostSwitchSurvivesReopenRejectsLostUpdatesAndNeverFallsBackFromCorruption()
    {
        var path = Path.Combine(Path.GetTempPath(), "TraderVI-host-test-" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var protector = new HostProtector(); var store = new BrokerExecutionHostGrantFileStore(path, protector);
            var grant = new BrokerExecutionHostGrant(Guid.NewGuid(), Guid.NewGuid(), new string('A',64),
                "fixture-build", DateTimeOffset.UtcNow, "fixture", true);
            Assert.Null(store.Read()); store.Write(null, grant);
            Assert.DoesNotContain("fixture-build", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            var reopened = new BrokerExecutionHostGrantFileStore(path, protector); Assert.Equal(grant, reopened.Read());
            Assert.Throws<InvalidOperationException>(() => reopened.Write(null, grant with { Enabled = false }));
            reopened.Write(grant, grant with { Enabled = false }); Assert.False(store.Read()!.Enabled);
            File.WriteAllText(path,"corrupt"); Assert.ThrowsAny<Exception>(() => reopened.Read());
        }
        finally { if(File.Exists(path))File.Delete(path); if(File.Exists(path+".lock"))File.Delete(path+".lock"); }
    }

    private sealed class HostClock(Fixture f) : TimeProvider { public override DateTimeOffset GetUtcNow() => f.Now; }
    private sealed class HostGrants : IBrokerExecutionHostGrantStore
    {
        internal BrokerExecutionHostGrant? Value;
        public BrokerExecutionHostGrant? Read() => Value;
        public void Write(BrokerExecutionHostGrant? expected, BrokerExecutionHostGrant next)
        { if(Value != expected)throw new InvalidOperationException("fixture conflict"); Value = next; }
    }
    private sealed class HostProtector : IManualBuyProtector
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        public byte[] Protect(byte[] data)
        {
            using var aes = new AesGcm(key,16); var nonce=RandomNumberGenerator.GetBytes(12); var result=new byte[data.Length+28];
            nonce.CopyTo(result,0); aes.Encrypt(nonce,data,result.AsSpan(28),result.AsSpan(12,16));return result;
        }
        public byte[] Unprotect(byte[] data)
        {
            using var aes = new AesGcm(key,16); var result=new byte[data.Length-28];
            aes.Decrypt(data.AsSpan(0,12),data.AsSpan(28),data.AsSpan(12,16),result);return result;
        }
    }
}
