#nullable enable
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerOpeningSessionPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Session = new(2026, 9, 23);
    private static readonly WealthsimpleTfsaAccount Account = new("fixture-account", "Fixture TFSA");
    private static ITsxSessionCalendar Calendar() => new ReviewedTsxSessionCalendar(new(
        "fixture", "offline", Session, Session, [Session]));
    private static WealthsimpleTfsaSnapshot Snapshot() => new(Account, Now, new(300, "CAD"), new(0, "CAD"),
        new(0, "CAD"), null, null, null, null, null, [
            new("AAA", "A", "EQUITY", 10, new(100, "CAD"), null, null) { SecurityId = "security-A" },
            new("BBB", "B", "EQUITY", 20, new(200, "CAD"), null, null) { SecurityId = "security-B" }]);
    private static BrokerOpeningSessionPlan Plan() => BrokerOpeningSessionPlan.FromSnapshot(Snapshot(), Session,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "fixture-build", "offline fixture", Now);
    private static BrokerOpeningStep Next(BrokerOpeningSessionPlan plan, WealthsimpleTfsaSnapshot? snapshot,
        ImmutableArray<ManualSellRecord> records, DateTimeOffset? now = null, WealthsimplePendingOrders? pending = null) =>
        BrokerOpeningSessionPolicy.Next(plan, snapshot, pending ?? new(Account, Now, []), records, Calendar(), now ?? Now);
    private static ManualSellRecord Sale(BrokerOpeningSessionPlan plan, BrokerOpeningHolding holding, bool resolved = true,
        decimal? filled = null)
    {
        decimal quantity = filled ?? holding.Quantity;
        var quote = new ManualBuyQuote(10.01m, 10, Now, "OPEN", Now.AddHours(-1), Now.AddHours(1), Now);
        var intent = new ManualSellIntent(plan.SellRequestId(holding), "fixture-identity", plan.AccountId,
            holding.SecurityId, "Fixture TFSA", holding.Symbol, "XTSE", holding.Quantity, Now, quote, Now);
        var state = new WealthsimpleMarketOrderState(filled.HasValue ? "CANCELLED" : "FILLED", holding.Quantity,
            quantity, new(10, "CAD"), new(quantity * 10, "CAD"), new(quantity * 10, "CAD"), new(0, "CAD"),
            new(0, "CAD"), Now, Now, null, "REGULAR", Now);
        return new(intent, WealthsimpleSubmissionStatus.Acknowledged, [state], Now,
            InventoryVerifiedAt: resolved ? Now : null);
    }

    [Fact]
    public void BeforeOpenWaitsWithoutRequiringAnAccountReadAndAfterCloseNeverCreatesSales()
    {
        var plan = Plan(); var bounds = Calendar().GetSessionBounds(Session);
        Assert.Equal("Waiting", Next(plan, null, [], bounds.OpenUtc.AddSeconds(-1)).Kind);
        Assert.Equal("Blocked", Next(plan, Snapshot(), [], bounds.CloseUtc).Kind);
        var step = Next(plan, Snapshot(), []);
        Assert.Equal("Sell", step.Kind); Assert.Equal(plan.Holdings[0], step.Holding);
        Assert.Equal(plan.SellRequestId(plan.Holdings[0]), step.RequestId);
    }

    [Fact]
    public void RecoveryKeepsOriginalIdAndPrecedesAnyFurtherSaleEvenAfterClose()
    {
        var plan = Plan(); var saved = Sale(plan, plan.Holdings[0], resolved: false);
        var step = Next(plan, null, [saved], Calendar().GetSessionBounds(Session).CloseUtc);
        Assert.Equal("Recover", step.Kind); Assert.Equal(saved.Intent.RequestId, step.RequestId);
        Assert.Equal("Blocked", Next(plan, Snapshot(), [saved with {
            Intent = saved.Intent with { Quantity = saved.Intent.Quantity + 1 } }]).Kind);
    }

    [Fact]
    public void ConfirmedFirstSaleNeedsFreshRemainingInventoryBeforeSecondSale()
    {
        var plan = Plan(); var first = Sale(plan, plan.Holdings[0]); var snapshot = Snapshot();
        Assert.Equal("Blocked", Next(plan, snapshot, [first]).Kind);
        var next = Next(plan, snapshot with { Holdings = [snapshot.Holdings[1]] }, [first]);
        Assert.Equal("Sell", next.Kind); Assert.Equal(plan.Holdings[1], next.Holding);
        Assert.Equal(plan.SellRequestId(plan.Holdings[1]), next.RequestId);
    }

    [Fact]
    public void FullSalesAndEmptyVerifiedInventoryOnlyPrepareTheNextReview()
    {
        var plan = Plan(); var sales = plan.Holdings.Select(h => Sale(plan, h)).ToImmutableArray();
        Assert.Equal("PrepareAccount", Next(plan, Snapshot() with { Holdings = [] }, sales).Kind);
        Assert.Equal("Blocked", Next(plan, Snapshot(), [Sale(plan, plan.Holdings[0], filled: 4)]).Kind);
    }

    [Theory]
    [InlineData("stale")][InlineData("future")][InlineData("account")][InlineData("pending")]
    [InlineData("quantity")][InlineData("instrument")][InlineData("outside-holding")]
    public void ChangedOrUnverifiedAccountNeverExpandsTheReviewedLiquidation(string failure)
    {
        var plan = Plan(); var snapshot = Snapshot(); var pending = new WealthsimplePendingOrders(Account, Now, []);
        switch (failure)
        {
            case "stale": snapshot = snapshot with { RetrievedAt = Now.AddSeconds(-31) }; break;
            case "future": snapshot = snapshot with { RetrievedAt = Now.AddSeconds(1) }; break;
            case "account": snapshot = snapshot with { Account = new("outside-account", "Other") }; break;
            case "pending": pending = pending with { Orders = [new("AAA", "SELL", "PENDING", "MARKET", 1, null, Now)] }; break;
            case "quantity": snapshot = snapshot with { Holdings = [snapshot.Holdings[0] with { Quantity = 11 }, snapshot.Holdings[1]] }; break;
            case "instrument": snapshot = snapshot with { Holdings = [snapshot.Holdings[0] with { SecurityId = "outside-security" }, snapshot.Holdings[1]] }; break;
            default: snapshot = snapshot with { Holdings = [.. snapshot.Holdings, snapshot.Holdings[0] with { Symbol = "OUTSIDE" }] }; break;
        }
        Assert.Contains(Next(plan, snapshot, [], pending: pending).Kind, new[] { "ReadAccount", "Blocked" });
    }

    [Fact]
    public void PreparingAPlanRejectsStaleFractionalOrMissingInstrumentFacts()
    {
        BrokerOpeningSessionPlan Create(WealthsimpleTfsaSnapshot snapshot) => BrokerOpeningSessionPlan.FromSnapshot(
            snapshot, Session, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "fixture", "fixture", Now);
        var snapshot = Snapshot();
        Assert.Throws<InvalidOperationException>(() => Create(snapshot with { RetrievedAt = Now.AddSeconds(-31) }));
        Assert.Throws<InvalidOperationException>(() => Create(snapshot with { Holdings = [snapshot.Holdings[0] with { Quantity = 1.5m }, snapshot.Holdings[1]] }));
        Assert.Throws<InvalidOperationException>(() => Create(snapshot with { Holdings = [snapshot.Holdings[0] with { SecurityId = null }, snapshot.Holdings[1]] }));
    }

    [Fact]
    public void ProtectedPlanIsImmutableAndRecoversTheSameRequestIdsAfterReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), "TraderVI-opening-plan-test-" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            var protector = new Protector(); var store = new BrokerOpeningPlanFileStore(path, protector); var plan = Plan();
            Assert.Null(store.Read()); store.SaveNew(plan);
            Assert.DoesNotContain("fixture-account", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            var read = new BrokerOpeningPlanFileStore(path, protector).Read()!; read.Validate();
            Assert.Equal(plan.PlanId, read.PlanId); Assert.Equal(plan.BaselineId, read.BaselineId);
            Assert.Equal(plan.SellRequestId(plan.Holdings[0]), read.SellRequestId(read.Holdings[0]));
            Assert.Throws<InvalidOperationException>(() => store.SaveNew(plan with { PlanId = Guid.NewGuid() }));
            var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
            Assert.ThrowsAny<CryptographicException>(() => store.Read());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class Protector : IManualBuyProtector
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        public byte[] Protect(byte[] data)
        {
            using var aes = new AesGcm(key, 16); var nonce = RandomNumberGenerator.GetBytes(12);
            var result = new byte[data.Length + 28]; nonce.CopyTo(result, 0);
            aes.Encrypt(nonce, data, result.AsSpan(28), result.AsSpan(12, 16)); return result;
        }
        public byte[] Unprotect(byte[] data)
        {
            using var aes = new AesGcm(key, 16); var result = new byte[data.Length - 28];
            aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), result); return result;
        }
    }
}
