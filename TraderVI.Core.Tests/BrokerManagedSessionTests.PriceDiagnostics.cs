#nullable enable
using Core.Runtime;
using Core.Trader.Promotion;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task RejectedBuyRetainsSelectedQuoteProvenanceAndBrokerTimesAcrossArchiveAndRestart(bool askAvailable)
    {
        var x = ContinuedFixture(); await x.Workflow().ApproveAsync(x.Approval()); OfferContinuationSignal(x);
        string permission = x.F.Store.State!.Continuation!.Fingerprint;
        decimal reference = askAvailable ? 105.01m : 105m;
        var quote = new TradingQuote("ABC", x.F.Now.UtcDateTime, x.F.Now.UtcDateTime.AddMilliseconds(125),
            105m, 104.99m, askAvailable ? 105.01m : null, "TmxQuoteFieldsV1");
        x.F.QuoteRead = _ => [quote];
        var broker = new BrokerBuyPriceComparison(reference, 105.03m, x.F.Now.AddSeconds(-4),
            x.F.Now.AddMilliseconds(450), x.F.Now.AddMilliseconds(550));
        var original = BrokerOrderFailureEvidence.Tag("PRIVATE BROKER PAYLOAD", BrokerOrderCheck.AskAboveDecisionReference,
            priceComparison: broker);
        x.F.Transport.BeforePrepare = () => throw original;
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => x.F.Workflow().TickAsync(x.F.Binding.BindingId)));
        var state = x.F.Store.State!;
        var prices = Assert.IsType<BrokerBuyPriceComparison>(state.LatestDecision!.RejectedBuyPrices);
        Assert.Equal(broker with { ReferenceQuote = prices.ReferenceQuote }, prices);
        Assert.Equal(new BrokerDecisionReferenceQuote("TmxQuoteFieldsV1", askAvailable ? "Ask" : "Price",
            quote.RequestedUtc, quote.ReceivedUtc, null), prices.ReferenceQuote);
        Assert.Null(prices.ReferenceQuote!.SourceQuotedUtc);
        Assert.Empty(state.Orders); Assert.Equal(0, x.F.Transport.Sent);
        Assert.Equal(permission, state.Continuation!.Fingerprint);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(state.LatestDecision));
        Assert.DoesNotContain("105.03", state.LatestDecision.Summary);
        string saved = JsonSerializer.Serialize(state);
        var archive = EngineArchiveCodec.Pack(saved);
        var restored = JsonSerializer.Deserialize<BrokerManagedSession>(EngineArchiveCodec.Restore(archive.Manifest, archive.Contents));
        Assert.Equal(saved, JsonSerializer.Serialize(restored));
        x.F.Store.Restart();
        Assert.Equal(prices, x.F.Store.State!.LatestDecision!.RejectedBuyPrices);
        Assert.Equal(permission, x.F.Store.State.Continuation!.Fingerprint);
    }

    [Fact]
    public async Task DiagnosticSaveFailurePreservesTheRejectionAndCannotReserveOrRetryAnOrder()
    {
        var x = ContinuedFixture(); await x.Workflow().ApproveAsync(x.Approval()); OfferContinuationSignal(x);
        var original = BrokerOrderFailureEvidence.Tag("Original failure", BrokerOrderCheck.AskAboveDecisionReference,
            priceComparison: new(105.01m, 105.03m, x.F.Now, x.F.Now, x.F.Now));
        int attempts = 0;
        x.F.Transport.BeforePrepare = () => { attempts++; throw original; };
        x.F.Store.BeforeAppend = state => { if (state.Event == "DecisionStageFailed") throw new InvalidOperationException("Save failed"); };
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => x.F.Workflow().TickAsync(x.F.Binding.BindingId)));
        Assert.Equal(1, attempts); Assert.Equal(0, x.F.Transport.Sent); Assert.Empty(x.F.Store.State!.Orders);
        Assert.Null(x.F.Store.State.LatestDecision!.RejectedBuyPrices);
    }

    [Fact]
    public async Task OtherRejectionsAndOldTracesDoNotAcquirePriceEvidence()
    {
        var x = ContinuedFixture(); await x.Workflow().ApproveAsync(x.Approval()); OfferContinuationSignal(x);
        x.F.Transport.BeforePrepare = () => throw BrokerOrderFailureEvidence.Tag("Unchanged risk hold", BrokerOrderCheck.RiskChanged,
            priceComparison: new(105.01m, 105.03m, x.F.Now, x.F.Now, x.F.Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.F.Workflow().TickAsync(x.F.Binding.BindingId));
        var trace = x.F.Store.State!.LatestDecision!;
        Assert.Null(trace.RejectedBuyPrices);
        string legacy = JsonSerializer.Serialize(trace);
        Assert.DoesNotContain("RejectedBuyPrices", legacy);
        Assert.Equal(legacy, JsonSerializer.Serialize(JsonSerializer.Deserialize<BrokerDecisionTrace>(legacy)));
        Assert.Empty(x.F.Store.State.Orders); Assert.Equal(0, x.F.Transport.Sent);
    }
}
