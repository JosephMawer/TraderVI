using Core.Runtime;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class EngineBrokerTransportTests
{
    [Fact]
    public async Task ActualBrokerDispatchReconcilesThroughPipeAndRetainsSelectedAccountIdentity()
    {
        var native = new NativeReads(); var protector = new Protector();
        var source = new EngineBrokerClient(native, protector); await source.StartAsync();
        using var journal = new Journal();
        var service = new EngineBrokerService(source, journal, protector, new Grants(), null,
            (_, _) => throw new InvalidOperationException("No financial workflow is authorized by this test."));
        await WithServer(service.DispatchAsync, async rpc =>
        {
            var display = new EngineBrokerClient(rpc, protector); await display.StartAsync();
            var account = Assert.Single((await display.ReadTfsaAccountsAsync()).Data);
            Assert.NotSame(native.Account, account); Assert.Equal(native.Account.Id, account.Id);
            Assert.Same(account, (await display.ReadTfsaSnapshotAsync(account)).Data.Account);
            Assert.Same(account, (await display.ReadTfsaPendingOrdersAsync(account)).Data.Account);
            var daily = (await display.ReadTfsaDailyPerformanceAsync(account)).Data;
            Assert.Same(account, daily.Account); Assert.Equal(.01m, daily.Rate);
            using var orders = new EngineOrderReadModel(rpc);
            var read = await new EngineReconciliationWorkflow(rpc, orders).ReadAsync(account, display.SessionRevision);
            Assert.True(read.Reconciliation.HoldingsMatch); Assert.NotNull(journal.Saved);
            Assert.Same(account, read.Snapshot.Account); Assert.Same(account, read.PendingOrders.Account);
            Assert.Empty(orders.ReadAll()); Assert.Empty(orders.ReadSells());
            // A copied ID is not an engine-issued selection, even for the same account name.
            await Assert.ThrowsAsync<InvalidOperationException>(() => display.ReadTfsaSnapshotAsync(new(account.Id, account.DisplayName)));
            native.Revision++; await source.CheckAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => display.ReadTfsaSnapshotAsync(account));
            var current = Assert.Single((await display.ReadTfsaAccountsAsync()).Data);
            await Assert.ThrowsAsync<InvalidOperationException>(() => display.ReadTfsaSnapshotAsync(account));
            Assert.Same(current, (await display.ReadTfsaSnapshotAsync(current)).Data.Account);
        });
    }

    [Fact]
    public async Task PreparedReviewRetainsInstrumentIdsAndAccountGraphButNeverTransfersSubmissionProvenance()
    {
        var account = new WealthsimpleTfsaAccount("account-test", "Test TFSA");
        var now = DateTimeOffset.UtcNow;
        var holding = new WealthsimpleHolding("TEST", "Test", "EQUITY", 2, new(20, "CAD"), new(18, "CAD"), new(2, "CAD")) { SecurityId = "security-test" };
        var pending = new WealthsimplePendingOrder("TEST", "BUY", "PENDING", "MARKET", 1, null, now)
            { SecurityId = "security-test", OrderId = "order-test" };
        var snapshot = Snapshot(account, now) with { Holdings = [holding] };
        var security = new WealthsimpleCanadianSecurity("security-test", "TEST", "XTSE")
            { SourceClient = new object(), SourceIdentity = "private-source-identity" };
        var context = new ManualBuyContext(account, security, snapshot, new(account, now, [pending]),
            new(10, 9, now, "OPEN", now.AddHours(-1), now.AddHours(1), now));
        var prepared = new PreparedManualBuy(context, new("TEST", "Test origin", "Manual"), 7, now);
        var baseline = JsonSerializer.Serialize(snapshot);
        await WithServer(_ => Task.FromResult<object>(new ManualBuyPreparation(prepared, "Reviewed")), async rpc =>
        {
            var result = (await rpc.CallAsync<ManualBuyPreparation>(EngineOperation.PrepareBuy, new EnginePrepareBuy(account, prepared.Origin))).Prepared;
            Assert.Equal(prepared.ReviewId, result.ReviewId);
            Assert.Same(account, result.Context.Account); Assert.Same(account, result.Context.Snapshot.Account);
            Assert.Same(account, result.Context.PendingOrders.Account);
            Assert.Equal("security-test", result.Context.Security.Id); Assert.Equal(2, result.Context.HeldQuantity);
            Assert.Equal("security-test", result.Context.Snapshot.Holdings.Single().SecurityId);
            Assert.Equal("order-test", result.Context.PendingOrders.Orders.Single().OrderId);
            Assert.Equal("security-test", result.Context.PendingOrders.Orders.Single().SecurityId);
            Assert.Null(result.Context.Security.SourceClient); Assert.Null(result.Context.Security.SourceIdentity);
            Assert.NotNull(ManualBuyOrigin.FromHolding(result.Context.Snapshot.Holdings.Single()));
        });
        Assert.Equal(baseline, JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain("account-test", baseline); Assert.DoesNotContain("security-test", baseline);
        Assert.DoesNotContain("private-source-identity", JsonSerializer.Serialize(context, EngineRpcJson.Options()));
    }

    [Fact]
    public async Task MismatchedReturnedAccountCannotBeReboundToSelectedAccount()
    {
        var selected = new WealthsimpleTfsaAccount("selected", "Test TFSA");
        var other = new WealthsimpleTfsaAccount("other", "Test TFSA");
        await WithServer(_ => Task.FromResult<object>(Snapshot(other, DateTimeOffset.UtcNow)), async rpc =>
            await Assert.ThrowsAsync<JsonException>(() => rpc.CallAsync<WealthsimpleTfsaSnapshot>(EngineOperation.AccountSnapshot, selected)));
    }

    [Fact]
    public void MessageLocalIdentityCacheDoesNotConflateOtherHandlesOrAcceptConflictingReferences()
    {
        var account = new WealthsimpleTfsaAccount("account-test", "Test TFSA");
        var reference = EngineRpcJson.Reference(account);
        var json = JsonSerializer.Serialize(new[] { account, account }, EngineRpcJson.Options());
        var result = JsonSerializer.Deserialize<WealthsimpleTfsaAccount[]>(json, EngineRpcJson.Options());
        Assert.Same(result[0], result[1]); Assert.NotSame(account, result[0]);
        var invalid = JsonSerializer.Serialize(new[] { reference, reference with { Id = "changed" } });
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WealthsimpleTfsaAccount[]>(invalid, EngineRpcJson.Options()));
        var catalogue = new EngineAccountCatalogue(); catalogue.Publish([account], 7);
        Assert.Throws<InvalidOperationException>(() => catalogue.Resolve(EngineRpcJson.Reference(account), 7, false));
        catalogue.Publish([], 7); catalogue.Publish([account], 7);
        Assert.Throws<InvalidOperationException>(() => catalogue.Resolve(reference, 7, true));
    }

    private static async Task WithServer(Func<EngineRequest, Task<object>> dispatch, Func<IEngineRpc, Task> test)
    {
        using var stop = new CancellationTokenSource();
        var name = "tradervi-account-test-" + Guid.NewGuid().ToString("N");
        var running = new EngineRpcServer(name, dispatch).RunAsync(stop.Token);
        try { await test(new EngineRpcClient(name)); }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    private static WealthsimpleTfsaSnapshot Snapshot(WealthsimpleTfsaAccount account, DateTimeOffset now) =>
        new(account, now, new(100, "CAD"), new(100, "CAD"), new(100, "CAD"), null, null, null, null, null, []);

    private sealed class NativeReads : IEngineRpc
    {
        internal WealthsimpleTfsaAccount Account { get; } = new("account-test", "Test TFSA");
        internal long Revision = 7;
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public Task<T> CallAsync<T>(EngineOperation operation, object payload = null, CancellationToken ct = default)
        {
            if (payload is WealthsimpleTfsaAccount account) Assert.Same(Account, account);
            object value = operation switch
            {
                EngineOperation.Connection => new EngineConnectionStatus(DateTimeOffset.UtcNow, true, true, Revision, true, false, false, false, "Connected", "Verified", "Verified"),
                EngineOperation.Accounts => new WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>(WealthsimpleReadStatus.Success, [Account]),
                EngineOperation.AccountSnapshot => new WealthsimpleReadResult<WealthsimpleTfsaSnapshot>(WealthsimpleReadStatus.Success, Snapshot(Account, now)),
                EngineOperation.PendingOrders => new WealthsimpleReadResult<WealthsimplePendingOrders>(WealthsimpleReadStatus.Success, new(Account, now, [])),
                EngineOperation.DailyPerformance => new WealthsimpleReadResult<WealthsimpleDailyPerformance>(WealthsimpleReadStatus.Success, new(Account, now, now, now.AddDays(-1), new(1, "CAD"), .01m)),
                EngineOperation.OrderHistory => new WealthsimpleReadResult<BrokerAccountHistory>(WealthsimpleReadStatus.Success, new("identity-test", Account.Id, now, [], []) { ActivityFingerprint = "unchanged" }),
                EngineOperation.ActivityCheckpoint => new WealthsimpleReadResult<BrokerActivityCheckpoint>(WealthsimpleReadStatus.Success, new("identity-test", Account.Id, "unchanged", now)),
                _ => throw new InvalidOperationException("Unexpected native operation in transport test.")
            };
            return Task.FromResult((T)value);
        }
    }
    private sealed class Protector : IWealthsimpleSessionProtector
    {
        public byte[] Protect(byte[] plaintext) => throw new NotSupportedException();
        public byte[] Unprotect(byte[] ciphertext) => throw new NotSupportedException();
    }
    private sealed class Grants : IBrokerExecutionHostGrantStore
    {
        public BrokerExecutionHostGrant Read() => null;
        public void Write(BrokerExecutionHostGrant expected, BrokerExecutionHostGrant next) => throw new NotSupportedException();
    }
    private sealed class Journal : IBrokerOrderJournal
    {
        internal BrokerAccountReconciliation Saved;
        public IReadOnlyList<ManualBuyRecord> ReadAll() => [];
        public IReadOnlyList<ManualSellRecord> ReadSells() => [];
        public bool HasUnresolvedOrders() => false;
        public void Save(ManualBuyRecord record) => throw new NotSupportedException();
        public void SaveSell(ManualSellRecord record) => throw new NotSupportedException();
        public BrokerAccountReconciliation ReadReconciliation(string identityId, string accountId) => Saved;
        public void SaveReconciliation(BrokerAccountReconciliation reconciliation) => Saved = reconciliation;
        public void Dispose() { }
    }
}
