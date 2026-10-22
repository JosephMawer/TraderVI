using Core.Runtime;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class EngineBrokerClientTests
{
    [Fact]
    public async Task ClosingAndReconnectingDashboardDoesNotStopOrDisconnectEngine()
    {
        var rpc = new Rpc(); var client = new EngineBrokerClient(rpc, new Protector());
        await client.StartAsync(); Assert.True(client.IsConnected);
        await client.StopAsync(); Assert.False(client.IsConnected);
        Assert.DoesNotContain(EngineOperation.Stop, rpc.Calls);
        Assert.DoesNotContain(EngineOperation.Disconnect, rpc.Calls);
        await client.StartAsync(); Assert.True(client.IsConnected); Assert.Equal(7, client.SessionRevision);
        Assert.All(rpc.Calls, operation => Assert.Equal(EngineOperation.Connection, operation));
    }

    [Fact]
    public async Task StaleConnectionCannotRemainGreenAndRawOrderSubmissionIsForbidden()
    {
        var rpc = new Rpc { Age = TimeSpan.FromMinutes(1) };
        var client = new EngineBrokerClient(rpc, new Protector());
        await client.StartAsync(); Assert.False(client.IsConnected); Assert.False(client.HasSession);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitManualBuyAsync(null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitManualSellAsync(null, default));
        Assert.Single(rpc.Calls);
    }

    [Fact]
    public async Task LostConnectionClearsDisplayReadinessInsteadOfReusingLastSuccess()
    {
        var rpc = new Rpc(); var client = new EngineBrokerClient(rpc, new Protector());
        await client.StartAsync(); Assert.True(client.IsConnected);
        rpc.Fail = true; await client.CheckAsync(); Assert.False(client.IsConnected);
        Assert.Equal("Engine disconnected", client.Status);
    }

    [Fact]
    public async Task JournalReadModelRequiresExplicitRefreshAndCannotWriteOrders()
    {
        var rpc = new Rpc(); using var journal = new EngineOrderReadModel(rpc);
        Assert.Throws<InvalidOperationException>(() => journal.ReadAll());
        await journal.RefreshAsync(); Assert.Empty(journal.ReadAll()); Assert.Empty(journal.ReadSells());
        Assert.False(journal.HasUnresolvedOrders());
        Assert.Throws<InvalidOperationException>(() => journal.Save(null));
        Assert.Throws<InvalidOperationException>(() => journal.SaveSell(null));
        Assert.Throws<InvalidOperationException>(() => journal.SaveReconciliation(null));
        Assert.Equal(EngineOperation.Orders, Assert.Single(rpc.Calls));
    }

    private sealed class Rpc : IEngineRpc
    {
        public List<EngineOperation> Calls { get; } = [];
        public TimeSpan Age { get; set; }
        public bool Fail { get; set; }
        public Task<T> CallAsync<T>(EngineOperation operation, object payload = null, CancellationToken ct = default)
        {
            Calls.Add(operation);
            if (Fail) throw new System.IO.IOException("not connected");
            object result = operation == EngineOperation.Orders ? new EngineOrders([], []) :
                new EngineConnectionStatus(DateTimeOffset.UtcNow - Age, true, true, 7, true,
                    false, false, false, "Connected", "Verified", "Verified connection");
            return Task.FromResult((T)result);
        }
    }
    private sealed class Protector : IWealthsimpleSessionProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.ToArray();
        public byte[] Unprotect(byte[] ciphertext) => ciphertext.ToArray();
    }
}
