using Core.Trader.Promotion;
using Core.Runtime;
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public class BrokerMarketSymbolsTests
{
    [Fact]
    public void HeldAliasUsesCanonicalPricesAndSignalsWithoutDuplicatingBuyOrChangingProtectionIdentity()
    {
        var input = TradingRecommendationTests.LiveInput();
        var holding = new ReportedTradingHolding(Guid.NewGuid(), "ABC.TO", 2, 200m, input.TradingDate);
        var map = ImmutableDictionary<string,string>.Empty.Add("ABC.TO", "ABC");
        input = input with { CurrentHoldings = [holding], BrokerMarketSymbols = map,
            Quotes = [new("ABC.TO", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 105m, 104m, 106m, "TmxQuoteFieldsV1")] };
        var first = TradingRecommendationEngine.Evaluate(input);
        Assert.DoesNotContain("NetAssetValueUnavailable", first.EntryRiskBlocks);
        Assert.DoesNotContain(first.Recommendations, r => r.Action is "Buy" or "Add");
        Assert.Equal("ABC.TO", Assert.Single(first.Recommendations).Symbol);
        Assert.Equal(holding.PositionId, first.Memory.Symbols["ABC.TO"].PositionId);
        Assert.Equal(100m, first.Memory.Symbols["ABC.TO"].LiveProtection.AveragePurchasePrice);
        var reloaded = DelphiLiveLedgerJson.Deserialize<TradingRecommendationMemory>(DelphiLiveLedgerJson.Serialize(first.Memory));
        var next = TradingRecommendationEngine.Evaluate(input with { PreviousMemory = reloaded,
            Quotes = [new("ABC.TO", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 94m, 94m, 95m, "TmxQuoteFieldsV1")] });
        Assert.Equal("Sell", Assert.Single(next.Recommendations).Action);
        Assert.Equal("ABC.TO", Assert.Single(next.Recommendations).Symbol);
        Assert.Equal(holding.PositionId, Assert.Single(next.Recommendations).PositionId);
        Assert.Equal("ABC", input.LiveInputs[0].Input.Stock.Symbol);
        Assert.Equal("ABC.TO", input.CurrentHoldings[0].Symbol);
        Assert.Equal("ABC", BrokerMarketSymbols.Market(input, "ABC.TO"));
    }
}
