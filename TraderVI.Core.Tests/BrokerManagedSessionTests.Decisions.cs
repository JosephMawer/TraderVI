#nullable enable
using Core.Trader.Promotion;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public async Task MultipleBuyRecommendationsRecordSelectionDeferralWithoutSendingTwoOrders()
    {
        var x = ContinuedFixture(); await x.Workflow().ApproveAsync(x.Approval()); OfferContinuationSignal(x);
        var original = x.F.TransformInput!;
        var candidateId = Guid.NewGuid();
        x.F.TransformInput = input =>
        {
            var first = original(input);
            var extra = first.LiveInputs.Select(o =>
            {
                var source = o.Input; var daily = source.DailySetup!;
                var stock = new DelphiLiveFiveMinuteSeries("DEF", source.Stock.SessionDate, source.Stock.SessionOpenUtc,
                    source.Stock.OperationalContinuityStartUtc, source.Stock.Bars.Select(b => new DelphiLiveFiveMinuteBar(
                        Guid.NewGuid(), "DEF", b.SessionDate, b.StartUtc, b.EndUtc, b.Open, b.High, b.Low, b.Close,
                        b.Volume, b.ReceivedUtc, b.Provider, b.SourceContractVersion, b.Disposition)));
                return o with { Input = source with { EvaluationId = Guid.NewGuid(), Stock = stock,
                    DailySetup = new(daily.DelphiRunId, candidateId, daily.DailyStrategyVersionId, daily.CommonDelphiComposite,
                        daily.SourceLenses) { ComparisonAdmission = daily.ComparisonAdmission } } };
            });
            return first with { LiveInputs = first.LiveInputs.AddRange(extra), Quotes = first.Quotes.Add(first.Quotes[0] with { Symbol = "DEF" }) };
        };
        var result = await x.F.Workflow().TickAsync(x.F.Binding.BindingId);
        Assert.Single(result.Orders); Assert.Equal(1, x.F.Transport.Sent);
        var entries = result.LatestDecision!.Entries;
        Assert.Equal(2, entries.Length); Assert.All(entries, e => Assert.Equal("Recommended", e.Outcome));
        Assert.Single(entries, e => e.ExecutionSelection == "Selected");
        Assert.Single(entries, e => e.ExecutionSelection == "DeferredForEarlierRecommendation");
    }

    [Fact]
    public async Task UnexpectedQuotePhaseFailureKeepsThePreparedConfirmationAndFailureEvidence()
    {
        var x = ContinuedFixture(); await x.Workflow().ApproveAsync(x.Approval()); OfferContinuationSignal(x);
        x.F.QuoteRead = _ => throw new TimeoutException("PRIVATE QUOTE PHASE TEXT");
        await Assert.ThrowsAsync<TimeoutException>(() => x.F.Workflow().TickAsync(x.F.Binding.BindingId));
        var state = x.F.Store.State!;
        Assert.Equal("QuoteObservationFailed", state.LatestDecision!.SelectionOutcome);
        Assert.Equal("TimeoutException", state.LatestDecision.FailureType);
        Assert.True(state.Memory.Symbols["ABC"].LastLiveResult!.ConfirmedLiveEligible);
        Assert.Empty(state.Orders); Assert.Equal(0, x.F.Transport.Sent);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(state.LatestDecision));
    }

    [Theory]
    [InlineData("no-quote", "NoRecommendation")]
    [InlineData("accepted", "SubmissionAcknowledged")]
    [InlineData("uncertain", "SubmissionOutcomeUnknown")]
    public async Task DecisionTraceSurvivesRestartAndNeverClaimsAFill(string scenario, string outcome)
    {
        var x = ContinuedFixture();
        await x.Workflow().ApproveAsync(x.Approval());
        OfferContinuationSignal(x);
        if (scenario == "no-quote") x.F.QuoteRead = _ => [];
        if (scenario == "uncertain") x.F.Transport.ThrowOnSubmit = true;
        var result = await x.F.Workflow().TickAsync(x.F.Binding.BindingId);
        var trace = Assert.IsType<BrokerDecisionTrace>(result.LatestDecision);
        Assert.Equal(outcome, trace.SelectionOutcome);
        Assert.Equal(scenario == "no-quote" ? "QuoteNotReturned" : "Recommended", Assert.Single(trace.Entries).Outcome);
        Assert.Equal(scenario == "no-quote" ? 0 : 1, x.F.Transport.Sent);
        Assert.Empty(result.Account.Positions);
        Assert.DoesNotContain(result.Orders, o => o.Latest?.FilledQuantity > 0);
        string originalTrace = JsonSerializer.Serialize(trace);
        x.F.Store.Restart();
        Assert.Equal(originalTrace, JsonSerializer.Serialize(x.F.Store.State!.LatestDecision));
        Assert.Equal(x.F.Store.State.Continuation!.Fingerprint, result.Continuation!.Fingerprint);
    }

    [Fact]
    public async Task ExactPreflightRejectionIsRecordedWithoutExceptionPayloadOrAnyOrderReservation()
    {
        var x = ContinuedFixture();
        await x.Workflow().ApproveAsync(x.Approval()); OfferContinuationSignal(x);
        x.F.Transport.BeforePrepare = () => throw BrokerOrderFailureEvidence.Tag(
            "PRIVATE RAW PAYLOAD", BrokerOrderCheck.AskAboveDecisionReference);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => x.F.Workflow().TickAsync(x.F.Binding.BindingId));
        Assert.Equal("PRIVATE RAW PAYLOAD", error.Message); // Existing failure semantics are preserved.
        var state = x.F.Store.State!;
        Assert.Empty(state.Orders); Assert.Equal(0, x.F.Transport.Sent);
        Assert.Equal("PreflightFailed", state.LatestDecision!.SelectionOutcome);
        Assert.Equal(BrokerOrderCheck.AskAboveDecisionReference, state.LatestDecision.FailedOrderCheck);
        Assert.Equal("InvalidOperationException", state.LatestDecision.FailureType);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(state.LatestDecision));
        Assert.Equal("DecisionStageFailed", state.Event);
    }

    [Fact]
    public async Task LostIntentAcknowledgementRetainsTheTraceAndCannotCauseAutomaticResubmission()
    {
        var x = ContinuedFixture(); await x.Workflow().ApproveAsync(x.Approval()); OfferContinuationSignal(x);
        x.F.Store.LoseResponseAtEvent = "OrderIntentReserved";
        await Assert.ThrowsAnyAsync<Exception>(() => x.F.Workflow().TickAsync(x.F.Binding.BindingId));
        var reserved = Assert.Single(x.F.Store.State!.Orders);
        Assert.Equal("IntentReserved", x.F.Store.State.LatestDecision!.SelectionOutcome);
        x.F.Store.Restart();
        await x.F.Workflow().TickAsync(x.F.Binding.BindingId);
        Assert.Equal(reserved.RequestId, Assert.Single(x.F.Store.State!.Orders).RequestId);
        Assert.Equal(0, x.F.Transport.Sent);
        Assert.Equal("IntentReserved", x.F.Store.State.LatestDecision!.SelectionOutcome);
    }
}
