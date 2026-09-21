#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class BrokerAccountImportTests
{
    private static BrokerAccountReconciliation ListedObservation(string mic = "XTSE", string symbol = "FIX.TO") =>
        Observation(10, 101, Buy() with { Symbol = symbol }) with {
            Instruments = [new("security-fixture", symbol, mic, "CAD", "EQUITY")] };

    [Theory] [InlineData("XTSE", "FIX.TO")] [InlineData("XTSX", "FIX.V")]
    public void VerifiedCanadianListingProposesAliasWithoutRewritingEitherSource(string mic, string symbol)
    {
        var state = State(true); var observation = ListedObservation(mic, symbol);
        var plan = Plan(observation, state);
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        var row = Assert.Single(plan.Rows);
        Assert.Equal("Match recorded trade", row.Action); Assert.Equal("FIX", row.RecordedSymbol);
        Assert.Equal(symbol, row.Symbol); Assert.Equal(PositionId, row.LegacyPositionId);
        Assert.Equal("FIX", state.Legacy.Trades[0].Symbol); Assert.Equal(symbol, observation.Orders[0].Symbol);
        var restored = JsonSerializer.Deserialize<BrokerImportPlan>(JsonSerializer.Serialize(plan))!;
        Assert.Equal(plan.Fingerprint, restored.Fingerprint);
        Assert.True(Plan(restored.Observation, Linked(plan)).CanImport);
    }
    [Theory]
    [InlineData("missing")] [InlineData("wrong-id")] [InlineData("wrong-symbol")] [InlineData("wrong-mic")]
    [InlineData("wrong-currency")] [InlineData("wrong-type")] [InlineData("duplicate")]
    [InlineData("collision")] [InlineData("venue-suffix")]
    public void UnverifiedOrAmbiguousListingCannotMergeTickers(string change)
    {
        var o = ListedObservation(); var i = o.Instruments![0];
        o = change switch {
            "missing" => o with { Instruments = null },
            "duplicate" => o with { Instruments = [i, i] },
            "collision" => o with { Instruments = [i, i with { SecurityId = "other-security" }] },
            "venue-suffix" => o with { Instruments = [i with { PrimaryMic = "XTSX" }] },
            _ => o with { Instruments = [change switch {
                "wrong-id" => i with { SecurityId = "other-security" },
                "wrong-symbol" => i with { Symbol = "OTHER.TO" },
                "wrong-mic" => i with { PrimaryMic = "XNYS" },
                "wrong-currency" => i with { Currency = "USD" },
                _ => i with { SecurityType = "OPTION" } }] } };
        var plan = Plan(o, State(true)); Assert.False(plan.CanImport); Assert.Null(plan.Rows[0].LegacyTradeId);
    }
    [Fact] public void AliasDoesNotHideFinancialDifferencesOrAChangedListingDuringReview()
    {
        var observation = ListedObservation(); var state = State(true);
        var different = state with { Legacy = state.Legacy with { Trades = [state.Legacy.Trades[0] with { Gross = 102, Net = 103 }] } };
        var plan = Plan(observation, different);
        Assert.False(plan.CanImport); Assert.Equal(new[] { "gross", "net" }, Assert.Single(plan.Differences!).Fields);
        Assert.Null(plan.Rows[0].LegacyTradeId);
        var approved = Plan(observation, state);
        var changed = observation with { Instruments = [observation.Instruments![0] with { PrimaryMic = "XTSX" }] };
        Assert.NotEqual(approved.Fingerprint, Plan(changed, state).Fingerprint);
        Assert.False(Plan(changed, Linked(approved)).CanImport);
    }
    [Fact] public void AddingListingEvidenceDoesNotReportAnOrderHistoryCorrection()
    {
        var initial = Observation();
        var next = Plan(initial with { Instruments = [new("security-fixture", "FIX", "XTSE", "CAD", "EQUITY")] }, Linked(Plan(initial)));
        Assert.True(next.CanImport); Assert.Equal("Already saved", next.Rows[0].Action);
        Assert.Equal(initial.Orders, next.Observation.Orders);
    }
    [Fact] public void CompletedEtfRoundTripCanBeImportedWithItsActualCostsAndRawType()
    {
        var buy = Buy() with { SecurityType = "EXCHANGE_TRADED_FUND" };
        var sell = Sell(10) with { SecurityType = "EXCHANGE_TRADED_FUND" };
        var plan = Plan(Observation(0, 0, buy, sell));
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        Assert.All(plan.Observation.Orders, o => Assert.Equal("EXCHANGE_TRADED_FUND", o.SecurityType));
        Assert.Equal(0, plan.Observation.Holdings.Single().ReconstructedQuantity);
        Assert.Equal(18, plan.Observation.Holdings.Single().RealizedProceedsLessCost);
    }
    private static BrokerImportState ClosedLedger(decimal recordedSaleGross = 120, decimal recordedSaleNet = 119)
    {
        var state = State(true); var sale = Sell(10);
        return state with { Legacy = state.Legacy with {
            Positions = [state.Legacy.Positions[0] with { IsActive = false }],
            Trades = [state.Legacy.Trades[0], new(Guid.NewGuid(), PositionId, "FIX", "SELL",
                new(2026, 9, 16), sale.LastFilledAt, 10, recordedSaleGross, 1, recordedSaleNet)] } };
    }
    [Fact] public void UnmatchedSaleShowsBothAmountsWithoutClaimingTheClosedPositionIsWrong()
    {
        var state = ClosedLedger(125, 124); var observation = Observation(0, 0, Buy(), Sell(10));
        var plan = Plan(observation, state);
        Assert.False(plan.CanImport);
        var d = Assert.Single(plan.Differences!);
        Assert.Equal(new[] { "gross", "net" }, d.Fields);
        Assert.Equal(125, d.Recorded.Gross); Assert.Equal(124, d.Recorded.Net);
        Assert.Equal(120, d.Broker.GrossValue); Assert.Equal(119, d.Broker.NetValue);
        Assert.Contains(plan.Blocks, b => b.Contains("recorded gross, net differ"));
        Assert.DoesNotContain(plan.Blocks, b => b.Contains("tracked shares") || b.Contains("purchase history"));
        Assert.Null(plan.Rows.Single(r => r.Side == "SELL").LegacyTradeId);
        Assert.Equal(124, state.Legacy.Trades.Last().Net); Assert.Equal(119, observation.Orders.Last().NetValue);
        var restored = JsonSerializer.Deserialize<BrokerImportPlan>(JsonSerializer.Serialize(plan))!;
        Assert.Equal(plan.Fingerprint, restored.Fingerprint); Assert.False(restored.CanImport);
        Assert.NotEqual(plan.Fingerprint, (plan with { Differences = [] }).Fingerprint);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void RealRecordedPositionInconsistencyStillBlocksAlongsideAnUnmatchedTrade(bool quantity)
    {
        var state = State(true); var p = state.Legacy.Positions[0];
        state = state with { Legacy = state.Legacy with {
            Positions = [quantity ? p with { Quantity = 9 } : p with { CostBasis = 99 }],
            Trades = [state.Legacy.Trades[0] with { Gross = 110, Net = 111 }] } };
        var plan = Plan(state: state);
        Assert.False(plan.CanImport); Assert.Single(plan.Differences!);
        Assert.Contains(plan.Blocks, b => b.Contains("tracked shares or book value"));
    }
    [Fact] public void AmbiguousDifferenceDoesNotPickTheClosestFinancialAmount()
    {
        var first = Buy(); var second = Buy("second") with { CreatedAt = first.CreatedAt.AddMinutes(2), LastFilledAt = first.LastFilledAt!.Value.AddMinutes(2) };
        var state = State(true); state = state with { Legacy = state.Legacy with {
            Trades = [state.Legacy.Trades[0] with { Gross = 105, Net = 106, FillAt = null }] } };
        var plan = Plan(Observation(20, 202, first, second), state);
        Assert.False(plan.CanImport); Assert.Null(plan.Differences);
        Assert.All(plan.Rows, r => Assert.Null(r.LegacyTradeId));
    }
    [Fact] public void SameDayMatchedRoundTripUsesBrokerSequenceForRecordedPositionCheck()
    {
        var buy = Buy() with { CreatedAt = Now.AddMinutes(-30), LastFilledAt = Now.AddMinutes(-29) };
        var sale = Sell(10); var state = ClosedLedger();
        state = state with { Legacy = state.Legacy with {
            Positions = [state.Legacy.Positions[0] with { EntryDate = new(2026, 9, 16) }],
            Trades = [state.Legacy.Trades[1] with { FillAt = null },
                state.Legacy.Trades[0] with { TradingDate = new(2026, 9, 16), FillAt = null }] } };
        Assert.True(Plan(Observation(0, 0, buy, sale), state).CanImport);
    }
}
