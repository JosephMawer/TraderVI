using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public class BrokerHoldingQuoteTests
{
    [Theory]
    [InlineData("valid")][InlineData("stale")][InlineData("future")][InlineData("account")]
    [InlineData("security")][InlineData("quantity")][InlineData("closed")][InlineData("missing")]
    [InlineData("oldSnapshot")][InlineData("oldQuoteReceipt")][InlineData("unknownHolding")]
    public void ProtectionBidRequiresExactFreshBrokerPosition(string scenario)
    {
        var f = new ManualMarketSellWorkflowTests.Fixture();
        var context = f.Broker.Context;
        var now = f.Clock.Now.UtcDateTime;
        var holding = new ReportedTradingHolding(Guid.NewGuid(), "FIX.TO", 20, 200m, DateOnly.FromDateTime(now));
        var input = TradingRecommendationTests.LiveInput() with { EvaluatedUtc = now, SessionOpenUtc = now.AddHours(-1),
            SessionCloseUtc = now.AddHours(1), CurrentHoldings = [holding],
            BrokerHoldingSecurityIds = ImmutableDictionary<string,string>.Empty.Add("FIX.TO", context.Security.Id) };
        if (scenario == "stale") context = context with { Quote = context.Quote with { QuotedAt = f.Clock.Now.AddSeconds(-61) } };
        if (scenario == "future") context = context with { Quote = context.Quote with { QuotedAt = f.Clock.Now.AddSeconds(1) } };
        if (scenario == "account") context = context with { Account = new("different", "Different") };
        if (scenario == "security") input = input with { BrokerHoldingSecurityIds = ImmutableDictionary<string,string>.Empty.Add("FIX.TO", "different") };
        if (scenario == "quantity") input = input with { CurrentHoldings = [holding with { Quantity = 19 }] };
        if (scenario == "closed") context = context with { Quote = context.Quote with { MarketStatus = "CLOSED" } };
        if (scenario == "missing") context = context with { Quote = context.Quote with { Bid = 0 } };
        if (scenario == "oldSnapshot") context = context with { Snapshot = context.Snapshot with { RetrievedAt = f.Clock.Now.AddSeconds(-1) } };
        if (scenario == "oldQuoteReceipt") context = context with { Quote = context.Quote with { RetrievedAt = f.Clock.Now.AddSeconds(-1) } };
        if (scenario == "unknownHolding") input = input with { CurrentHoldings = [] };
        TradingQuote Read() => BrokerHoldingQuote.Validate(input, f.Selection.Account, "FIX.TO", now, now.AddSeconds(1), context);
        if (scenario != "valid") { Assert.Throws<InvalidOperationException>(() => Read()); return; }
        var quote = Read(); Assert.Equal(10m, quote.Bid); Assert.Null(quote.Ask); Assert.Null(quote.Price);
        Assert.Equal(BrokerHoldingQuote.Contract, quote.SourceContract);
        Assert.NotNull(TradingRecommendationEngine.Quote(input with { Quotes = [quote] }, "FIX.TO"));
        Assert.Null(TradingRecommendationEngine.Quote(input with { Quotes = [quote], CurrentHoldings = [] }, "FIX.TO"));
        var exitQuote = BrokerHoldingQuote.Validate(input, f.Selection.Account, "FIX.TO", now, now.AddSeconds(1),
            context with { Quote = context.Quote with { Bid = 9.4m } });
        var result = TradingRecommendationEngine.Evaluate(input with { Quotes = [exitQuote] });
        Assert.Equal("Sell", result.Recommendations.Single(r => r.Symbol == "FIX.TO").Action);
        Assert.Equal(holding.PositionId, result.Recommendations.Single(r => r.Symbol == "FIX.TO").PositionId);
        Assert.Equal(0, f.Broker.Submissions);
    }
}
