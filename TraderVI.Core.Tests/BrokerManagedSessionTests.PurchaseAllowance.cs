#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    // Reference behavior for the later compact writer. These observations exercise the
    // existing allowance calculation independently of eligibility to place another trade.
    [Theory]
    [InlineData("PENDING", 0, 0, 100)]
    [InlineData("PARTIALLY_FILLED", 4, 38, 100)]
    [InlineData("FILLED", 10, 96, 96)]
    [InlineData("POSTED", 10, 96, 96)]
    [InlineData("CANCELLED", 4, 38, 38)]
    [InlineData("EXPIRED", 0, 0, 0)]
    [InlineData("REJECTED", 0, 0, 0)]
    public async Task PurchaseAllowanceUsesOneOriginalReservationUntilReconciled(
        string status, int filled, int netCost, int expectedUsed)
    {
        var fixture = new Fixture();
        var session = await fixture.Workflow().ApproveAsync(fixture.Approval(ongoing: true));
        var buy = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-DDD",
            "DDD", "BUY", 10, 10m, "Initial", null, fixture.Now, WealthsimpleSubmissionStatus.Acknowledged);
        var fact = fixture.Fact(buy.ExternalId, buy.Symbol, buy.Side, filled, netCost, fixture.Now)
            with { SubmittedQuantity = buy.Quantity, Status = status };
        buy = buy with { Latest = fact, Reconciled = fact.IsTerminal };

        var observed = session with { Orders = [buy] };
        Assert.Equal((decimal)expectedUsed, observed.CommittedPurchaseCost);
    }

    [Fact]
    public async Task PurchaseAllowanceKeepsOriginalReservationAcrossUnknownReplySaleAndNextDay()
    {
        var fixture = new Fixture();
        var session = await fixture.Workflow().ApproveAsync(fixture.Approval(ongoing: true));
        var buy = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-DDD",
            "DDD", "BUY", 10, 10m, "Initial", null, fixture.Now);
        var sale = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "security-AAA",
            "AAA", "SELL", 5, 12m, "Exit", null, fixture.Now);
        sale = sale with { Latest = fixture.Fact(sale.ExternalId, sale.Symbol, sale.Side, 5, 60, fixture.Now),
            Reconciled = true, Submission = WealthsimpleSubmissionStatus.Acknowledged };

        var reserved = session with { Orders = [buy] };
        Assert.Equal(100m, reserved.CommittedPurchaseCost);
        fixture.Facts.Add(sale.Latest!);
        fixture.Cash += 60m;
        AdvanceVerifiedRiskDay(fixture);
        var nextDay = reserved with { SavedAt = fixture.Now, Account = fixture.Project(),
            Orders = [buy with { Submission = WealthsimpleSubmissionStatus.OutcomeUnknown }, sale] };
        Assert.Equal(reserved.Account.Account.TradingDate.AddDays(1), nextDay.Account.Account.TradingDate);
        Assert.Equal(100m, nextDay.CommittedPurchaseCost);
        Assert.Equal(800m, nextDay.EffectiveReview.PurchaseCeiling - nextDay.CommittedPurchaseCost);
        Assert.Equal(0, fixture.Transport.Sent);
    }
}
