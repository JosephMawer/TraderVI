#nullable enable

using Core.Trader.DelphiLive;
using Core.TMX.Models.Domain;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveOperationalPolicyTests
{
    private static readonly DelphiLivePolicyDefinition Policy =
        DelphiLivePolicyDefinition.Version1;

    [Fact]
    public void V1Policy_PinsEveryCrossLayerIdentityAndFrozenSafetyValue()
    {
        Policy.PolicyVersionId.ShouldBe(Guid.Parse("C15C1A27-13A1-581A-8912-06C92941A01E"));
        Policy.PolicyDefinitionName.ShouldBe("DelphiLivePolicyV1");
        Policy.EvaluatorVersion.ShouldBe("DelphiLiveEvaluatorV1");
        Policy.CollectorVersion.ShouldBe("IntradayEvidenceCollectorV3");
        Policy.DecisionDossierVersion.ShouldBe("DelphiLiveDecisionDossierV1");
        Policy.QuoteFillVersion.ShouldBe("DelphiLiveQuoteFillV1");
        Policy.MaximumHoldings.ShouldBe(5);
        Policy.EntryTargetNavFraction.ShouldBe(0.20m);
        Policy.FastDownsideReturnFloor.ShouldBe(-0.10m);
        Policy.PrimaryExitReasonOrder.ShouldBe([
            DelphiLiveExitRule.HardLoss5Pct,
            DelphiLiveExitRule.FastDownside10Pct,
            DelphiLiveExitRule.ProfitProtectionFloorBreach,
            DelphiLiveExitRule.ConfirmedSupportFailure,
            DelphiLiveExitRule.LiveWeakeningExit
        ]);
    }

    [Fact]
    public void DataConfidence_UsesOneTwoThreeMissLadderAndOneCleanRecovery()
    {
        DelphiLiveDataConfidence first = DelphiLiveDataConfidencePolicy.Advance(
            DelphiLiveDataConfidence.Normal, true, false);
        DelphiLiveDataConfidence second = DelphiLiveDataConfidencePolicy.Advance(first, true, false);
        DelphiLiveDataConfidence third = DelphiLiveDataConfidencePolicy.Advance(second, true, false);
        DelphiLiveDataConfidence recovered = DelphiLiveDataConfidencePolicy.Advance(third, false, true);

        first.State.ShouldBe(DelphiLiveDataConfidenceState.Ambiguous);
        second.State.ShouldBe(DelphiLiveDataConfidenceState.Degraded);
        third.State.ShouldBe(DelphiLiveDataConfidenceState.MonitoringLost);
        recovered.ShouldBe(DelphiLiveDataConfidence.Normal);
    }

    [Fact]
    public void DataConfidence_QuoteFailureAndLegitimateImmaturityDoNotCountAsMisses()
    {
        DelphiLiveDataConfidence current = new(DelphiLiveDataConfidenceState.Ambiguous, 1);

        DelphiLiveDataConfidencePolicy.Advance(current, false, false).ShouldBe(current);
    }

    [Fact]
    public void Schedule_ContainsEveryFiveMinuteEndpointAndUsesTwoMinuteOffset()
    {
        TimeZoneInfo toronto = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        IReadOnlyList<DateTime> ends = DelphiLiveSchedule.GetBarEndsUtc(
            new DateOnly(2026, 9, 8),
            toronto);

        ends.Count.ShouldBe(78);
        TimeZoneInfo.ConvertTimeFromUtc(ends[0], toronto).TimeOfDay.ShouldBe(new TimeSpan(9, 35, 0));
        TimeZoneInfo.ConvertTimeFromUtc(ends[^1], toronto).TimeOfDay.ShouldBe(new TimeSpan(16, 0, 0));
        TimeZoneInfo.ConvertTimeFromUtc(
            DelphiLiveSchedule.CollectionStartUtc(ends[0]), toronto).TimeOfDay
            .ShouldBe(new TimeSpan(9, 37, 0));
        DelphiLiveSchedule.IsBuyDecisionBar(ends[3], toronto).ShouldBeTrue(); // 09:50
        DelphiLiveSchedule.IsBuyDecisionBar(ends.Single(x =>
            TimeZoneInfo.ConvertTimeFromUtc(x, toronto).TimeOfDay == new TimeSpan(15, 45, 0)), toronto)
            .ShouldBeFalse();
    }

    [Fact]
    public void CollectionPriority_DeduplicatesAndProtectsCapitalBeforeCandidates()
    {
        IReadOnlyList<DelphiLiveObservationTarget> ordered =
            DelphiLiveCollectionPriorityPlanner.OrderAndDeduplicate([
                new("AAA", DelphiLiveCollectionPriorityClass.QuietOrDismissedCandidate, 4, true, false),
                new("BBB", DelphiLiveCollectionPriorityClass.ActiveCandidate, 2, true, false),
                new("XIU", DelphiLiveCollectionPriorityClass.XiuBenchmark, 0, false, false),
                new("AAA", DelphiLiveCollectionPriorityClass.HeldSymbol, 0, true, false),
                new("CCC", DelphiLiveCollectionPriorityClass.PendingProtectiveSell, 0, false, true)
            ]);

        ordered.Select(x => x.Symbol).ShouldBe(["CCC", "AAA", "XIU", "BBB"]);
        ordered[1].PriorityClass.ShouldBe(DelphiLiveCollectionPriorityClass.HeldSymbol);
    }

    [Fact]
    public void Continuity_MaturesAtFourFromOpenButFiveAfterGap()
    {
        DateTime firstEnd = Utc(2026, 9, 8, 13, 35);
        DelphiLiveContinuityState opening = DelphiLiveContinuityPolicy.Start(1, true);
        for (int index = 0; index < 4; index++)
            opening = DelphiLiveContinuityPolicy.Advance(opening, firstEnd.AddMinutes(index * 5), true);
        opening.FourFamilyEvaluationMayBeMature.ShouldBeTrue();

        DelphiLiveContinuityState resumed = DelphiLiveContinuityPolicy.Advance(opening, firstEnd.AddMinutes(20), false);
        for (int index = 0; index < 4; index++)
            resumed = DelphiLiveContinuityPolicy.Advance(resumed, firstEnd.AddMinutes(25 + index * 5), true);
        resumed.FourFamilyEvaluationMayBeMature.ShouldBeFalse();
        resumed = DelphiLiveContinuityPolicy.Advance(resumed, firstEnd.AddMinutes(45), true);
        resumed.FourFamilyEvaluationMayBeMature.ShouldBeTrue();
    }

    [Theory]
    [InlineData(DelphiLiveActionSide.Buy, 10.20, 10.00, 10.10, 10.20, DelphiLiveQuoteField.Ask, DelphiLiveFillConfidence.SideSpecific)]
    [InlineData(DelphiLiveActionSide.Sell, 10.20, 10.00, 10.10, 10.00, DelphiLiveQuoteField.Bid, DelphiLiveFillConfidence.SideSpecific)]
    public void QuoteFill_UsesTheSideNeededByTheAction(
        DelphiLiveActionSide side,
        double ask,
        double bid,
        double price,
        double expected,
        DelphiLiveQuoteField field,
        DelphiLiveFillConfidence confidence)
    {
        DelphiLiveActionIntent action = Action(side);
        DelphiLiveCausalQuoteObservation quote = Quote(action, 1, (decimal)price, (decimal)bid, (decimal)ask);

        DelphiLiveQuoteAttemptDecision decision =
            DelphiLiveExecutionPolicy.EvaluateQuoteAttempt(action, quote, Policy);

        decision.FillPrice.ShouldBe((decimal)expected);
        decision.SelectedField.ShouldBe(field);
        decision.Confidence.ShouldBe(confidence);
    }

    [Fact]
    public void QuoteFill_TagsPriceFallbackAsEstimatedFill()
    {
        DelphiLiveActionIntent action = Action(DelphiLiveActionSide.Buy);
        DelphiLiveCausalQuoteObservation quote = Quote(action, 1, 10.11m, null, null);

        DelphiLiveQuoteAttemptDecision decision =
            DelphiLiveExecutionPolicy.EvaluateQuoteAttempt(action, quote, Policy);

        decision.HasFill.ShouldBeTrue();
        decision.SelectedField.ShouldBe(DelphiLiveQuoteField.Price);
        decision.Confidence.ShouldBe(DelphiLiveFillConfidence.EstimatedFill);
    }

    [Fact]
    public void QuoteFill_RejectsReuseOfDecisionEvidence()
    {
        DelphiLiveActionIntent action = Action(DelphiLiveActionSide.Sell);
        DelphiLiveCausalQuoteObservation quote = Quote(action, 1, 10m, 10m, 10.1m) with
        {
            QuoteObservationId = action.DecisionEvidenceId
        };

        Should.Throw<ArgumentException>(() =>
            DelphiLiveExecutionPolicy.EvaluateQuoteAttempt(action, quote, Policy));
    }

    [Fact]
    public void QuoteFill_RejectsEvidenceWithoutPostDecisionReceipt()
    {
        DelphiLiveActionIntent action = Action(DelphiLiveActionSide.Buy);
        DelphiLiveCausalQuoteObservation quote = Quote(action, 1, 10m, 10m, 10.1m) with
        {
            RequestStartedUtc = action.DecisionPersistedUtc,
            ReceivedUtc = action.DecisionPersistedUtc
        };

        Should.Throw<ArgumentException>(() =>
            DelphiLiveExecutionPolicy.EvaluateQuoteAttempt(action, quote, Policy));
    }

    [Fact]
    public void QuoteFill_ExpiresBuyButKeepsProtectiveSellPendingAfterThreeFailures()
    {
        DelphiLiveActionIntent buy = Action(DelphiLiveActionSide.Buy);
        DelphiLiveActionIntent sell = Action(DelphiLiveActionSide.Sell);

        DelphiLiveExecutionPolicy.EvaluateQuoteAttempt(buy, Quote(buy, 3, null, null, null), Policy)
            .Disposition.ShouldBe(DelphiLiveQuoteAttemptDisposition.BuyQuoteUnavailableExpired);
        DelphiLiveExecutionPolicy.EvaluateQuoteAttempt(sell, Quote(sell, 3, null, null, null), Policy)
            .Disposition.ShouldBe(DelphiLiveQuoteAttemptDisposition.SellRemainsPending);
    }

    [Fact]
    public void PortfolioSizing_UsesTwentyPercentOfCurrentExactNavAndWholeShares()
    {
        var nav = new DelphiLiveNavResult(true, 1_000m, Array.Empty<string>(), "CompleteExactNav");
        var guards = new DelphiLivePortfolioGuardState(false, false, 0m, 0m, 1_000m);

        DelphiLiveBuySizingDecision decision = DelphiLivePortfolioPolicy.SizeWholeShareEntry(
            nav, 500m, 33m, 2, false, guards, Policy);

        decision.IsAllowed.ShouldBeTrue();
        decision.TargetNotional.ShouldBe(200m);
        decision.Quantity.ShouldBe(6);
        decision.RequiredCash.ShouldBe(198m);
    }

    [Fact]
    public void PortfolioSizing_BlocksRiskUntilOpeningNavIsAvailable()
    {
        var nav = new DelphiLiveNavResult(true, 1_000m, Array.Empty<string>(), "CompleteExactNav");
        DelphiLivePortfolioGuardState guards = DelphiLivePortfolioPolicy.EvaluateGuards(
            1_000m, null, 1_000m, false, false, Policy);

        DelphiLiveBuySizingDecision decision = DelphiLivePortfolioPolicy.SizeWholeShareEntry(
            nav, 500m, 33m, 2, false, guards, Policy);

        decision.IsAllowed.ShouldBeFalse();
        decision.ReasonCode.ShouldBe(DelphiLivePortfolioReasons.PortfolioNavUnavailable);
    }

    [Fact]
    public void ExactNav_DoesNotCarryForwardAMissingHoldingMark()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        DateTime end = Utc(2026, 9, 8, 14, 0);

        DelphiLiveNavResult nav = DelphiLivePortfolioPolicy.CalculateExactNav(
            100m,
            [(first, "AAA", 2), (second, "BBB", 3)],
            [new(first, "AAA", 2, 10m, end)],
            end);

        nav.IsComplete.ShouldBeFalse();
        nav.NetAssetValue.ShouldBeNull();
        nav.MissingSymbols.ShouldBe(["BBB"]);
    }

    [Fact]
    public void PortfolioActions_ProcessProtectiveSellsBeforeRankedBuys()
    {
        IReadOnlyList<DelphiLivePortfolioActionCandidate> ordered =
            DelphiLivePortfolioPolicy.OrderCapitalFirst([
                new(Guid.NewGuid(), DelphiLiveActionSide.Buy, 1, "AAA"),
                new(Guid.NewGuid(), DelphiLiveActionSide.Sell, 50, "ZZZ"),
                new(Guid.NewGuid(), DelphiLiveActionSide.Buy, 2, "BBB")
            ]);

        ordered.Select(x => x.Side).ShouldBe([
            DelphiLiveActionSide.Sell,
            DelphiLiveActionSide.Buy,
            DelphiLiveActionSide.Buy
        ]);
    }

    [Fact]
    public void FrozenSource_UsesNewestQualifyingRunAndPreservesBothLensRows()
    {
        DateOnly date = new(2026, 9, 8);
        DateTime cutoff = Utc(2026, 9, 8, 13, 30);
        Guid strategy = Guid.NewGuid();
        DelphiLiveOfficialRunSource older = Run(Guid.NewGuid(), strategy, date, cutoff.AddMinutes(-20));
        DelphiLiveOfficialRunSource selected = Run(Guid.NewGuid(), strategy, date, cutoff.AddMinutes(-5));
        DelphiLiveOfficialRunSource late = Run(Guid.NewGuid(), strategy, date, cutoff.AddMinutes(1));
        Guid candidateId = Guid.NewGuid();
        var candidate = new DelphiLiveCandidateSource(candidateId, selected.RunId, "AAA", 0.77m, "{}");

        DelphiLiveFrozenWatchlist frozen = DelphiLiveFrozenSourceSelector.Freeze(
            date,
            date.AddDays(-1),
            cutoff,
            [older, selected, late],
            [candidate],
            [
                Lens(candidateId, "Continuation", 3),
                Lens(candidateId, "Breakout", 7)
            ]);

        frozen.Run!.RunId.ShouldBe(selected.RunId);
        frozen.Candidates.Count.ShouldBe(1);
        frozen.Candidates[0].SourceLenses.Count.ShouldBe(2);
        frozen.Candidates[0].BestSourceRank.ShouldBe(3);
    }

    [Fact]
    public void FrozenSource_FailsClosedWhenMarketDataIsNotImmediatelyPrecedingSession()
    {
        DateOnly date = new(2026, 9, 8);
        DateTime cutoff = Utc(2026, 9, 8, 13, 30);
        DelphiLiveOfficialRunSource stale = Run(Guid.NewGuid(), Guid.NewGuid(), date, cutoff.AddMinutes(-5)) with
        {
            MarketDataAsOf = date.AddDays(-2)
        };

        DelphiLiveFrozenWatchlist frozen = DelphiLiveFrozenSourceSelector.Freeze(
            date, date.AddDays(-1), cutoff, [stale], [], []);

        frozen.Status.ShouldBe("NoValidDelphiRun");
        frozen.AllowsNewRisk.ShouldBeFalse();
    }

    [Fact]
    public void ReceiptNormalization_UsesExactBarAndDeadlineInsteadOfNearestInterval()
    {
        DateTime start = Utc(2026, 9, 8, 13, 30);
        DateTime end = start.AddMinutes(5);
        var request = new DelphiLiveMarketDataRequest(
            Guid.NewGuid(), "AAA", start, end, end.AddMinutes(7), end.AddMinutes(2), 1);
        var stale = new DelphiLiveMarketDataReceipt(
            request,
            new OhlcvBar(start.AddMinutes(-5), 10m, 11m, 9m, 10m, 1_000),
            end.AddMinutes(3),
            "raw");

        DelphiLiveCollectionWorkflow.NormalizeReceipt(request, stale, end.AddMinutes(7))
            .Disposition.ShouldBe(DelphiLiveCollectionDispositions.StaleNoNewBar);
    }

    private static DelphiLiveActionIntent Action(DelphiLiveActionSide side)
    {
        DateTime decision = Utc(2026, 9, 8, 14, 0);
        return new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AAA",
            side,
            decision,
            decision.AddMilliseconds(1),
            side == DelphiLiveActionSide.Buy ? Utc(2026, 9, 8, 19, 45) : null,
            side == DelphiLiveActionSide.Buy ? 10 : null,
            side == DelphiLiveActionSide.Buy ? 100m : null);
    }

    private static DelphiLiveCausalQuoteObservation Quote(
        DelphiLiveActionIntent action,
        int attempt,
        decimal? price,
        decimal? bid,
        decimal? ask)
    {
        DateTime request = action.DecisionPersistedUtc.AddSeconds(attempt);
        return new(
            Guid.NewGuid(),
            action.DecisionId,
            action.Symbol,
            attempt,
            request,
            request.AddMilliseconds(100),
            price,
            bid,
            ask,
            "TmxQuoteV1");
    }

    private static DelphiLiveOfficialRunSource Run(
        Guid id,
        Guid strategy,
        DateOnly date,
        DateTime createdUtc) =>
        new(
            id,
            strategy,
            "OfficialPaper",
            "Valid",
            date,
            date.AddDays(-1),
            createdUtc.AddMinutes(-1),
            createdUtc);

    private static DelphiLiveLensSource Lens(Guid candidateId, string lens, int rank) =>
        new(Guid.NewGuid(), candidateId, lens, true, true, rank, 1m - rank / 100m, null, "{}");

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
