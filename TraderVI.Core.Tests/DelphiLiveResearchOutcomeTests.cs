#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveResearchOutcomeTests
{
    private static readonly DateOnly SessionDate = new(2026, 9, 4);
    private static readonly DateTime OpenUtc = new(2026, 9, 4, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime CloseUtc = new(2026, 9, 4, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DelphiLivePolicyDefinition Policy = DelphiLivePolicyDefinition.Version1;

    [Fact]
    public void IntradayOutcomeStartsAfterAnchorAndKeepsExactReturnsAndPathExtremesSeparate()
    {
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", OpenUtc, 100m, 100m, 130m, 70m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", OpenUtc, 200m, 200m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC",
            anchor.EndUtc,
            100m,
            Enumerable.Range(1, 36).Select(index => 100m + index).ToArray());
        List<DelphiLiveFiveMinuteBar> xiu = BuildFuture(
            "XIU",
            xiuAnchor.EndUtc,
            200m,
            Enumerable.Range(1, 36).Select(index => 200m + index).ToArray());

        DelphiLiveObservationOutcome outcome = DelphiLiveObservationOutcomeCalculator.Calculate(
            Input(anchor, xiuAnchor, stock, xiu, asOfUtc: CloseUtc),
            Policy);

        DelphiLiveOutcomeHorizonResult twenty = outcome.Horizons.Single(
            result => result.Horizon == DelphiLiveOutcomeHorizon.Minutes20);
        twenty.RawReturn.RequireValue().ShouldBe(0.04m);
        twenty.XiuReturn.RequireValue().ShouldBe(0.02m);
        twenty.ExcessReturn.RequireValue().ShouldBe(0.02m);
        twenty.MaximumFavourableMovement.RequireValue().ShouldBe(0.0401m);
        twenty.MaximumAdverseMovement.RequireValue().ShouldBe(-0.0001m);
        twenty.OpportunityThresholds.Single(hit => hit.Threshold == 0.01m)
            .FirstIntervalEndUtc.ShouldBe(anchor.EndUtc.AddMinutes(5));

        // The anchor's deliberately extreme high and low are information already known.
        twenty.MaximumFavourableMovement.RequireValue().ShouldBeLessThan(0.30m);
        twenty.MaximumAdverseMovement.RequireValue().ShouldBeGreaterThan(-0.30m);
    }

    [Fact]
    public void MissingInteriorBarInvalidatesPathButNotAvailableExactEndpointReturn()
    {
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", OpenUtc, 100m, 100m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", OpenUtc, 200m, 200m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC",
            anchor.EndUtc,
            100m,
            new[] { 101m, 102m, 103m, 104m });
        stock.RemoveAt(1);
        List<DelphiLiveFiveMinuteBar> xiu = BuildFuture(
            "XIU",
            xiuAnchor.EndUtc,
            200m,
            new[] { 201m, 202m, 203m, 204m });

        DelphiLiveOutcomeHorizonResult result = DelphiLiveObservationOutcomeCalculator.Calculate(
                Input(anchor, xiuAnchor, stock, xiu, asOfUtc: anchor.EndUtc.AddMinutes(30)),
                Policy)
            .Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20);

        result.RawReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Valid);
        result.RawReturn.RequireValue().ShouldBe(0.04m);
        result.MaximumFavourableMovement.State.ShouldBe(DelphiLiveOutcomeMetricState.Invalid);
        result.MaximumFavourableMovement.ReasonCode.ShouldBe(DelphiLiveOutcomeReasons.MissingContiguousPath);
        result.OpportunityThresholds.ShouldAllBe(hit => hit.State == DelphiLiveOutcomeMetricState.Invalid);
    }

    [Fact]
    public void MissingXiuInvalidatesOnlyXiuDependentReturn()
    {
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", OpenUtc, 100m, 100m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", OpenUtc, 200m, 200m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC", anchor.EndUtc, 100m, new[] { 101m, 102m, 103m, 104m });

        DelphiLiveOutcomeHorizonResult result = DelphiLiveObservationOutcomeCalculator.Calculate(
                Input(anchor, xiuAnchor, stock, Array.Empty<DelphiLiveFiveMinuteBar>(),
                    asOfUtc: anchor.EndUtc.AddMinutes(30)),
                Policy)
            .Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20);

        result.RawReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Valid);
        result.XiuReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Invalid);
        result.ExcessReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Invalid);
        result.ExcessReturn.ReasonCode.ShouldBe(DelphiLiveOutcomeReasons.MissingMatchingXiu);
        result.MaximumFavourableMovement.State.ShouldBe(DelphiLiveOutcomeMetricState.Valid);
    }

    [Fact]
    public void MissingXiuAnchorPreservesRawStockOutcomesWithoutInventingBenchmarkFacts()
    {
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", OpenUtc, 100m, 100m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC", anchor.EndUtc, 100m, [101m, 102m, 103m, 104m]);

        DelphiLiveObservationOutcome outcome = DelphiLiveObservationOutcomeCalculator.Calculate(
            Input(anchor, null, stock, [], anchor.EndUtc.AddMinutes(30)), Policy);
        DelphiLiveOutcomeHorizonResult result = outcome.Horizons.Single(
            item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20);

        outcome.XiuAnchorObservationId.ShouldBeNull();
        outcome.XiuAnchorClose.ShouldBeNull();
        result.RawReturn.RequireValue().ShouldBe(0.04m);
        result.MaximumFavourableMovement.State.ShouldBe(DelphiLiveOutcomeMetricState.Valid);
        result.XiuReturn.ReasonCode.ShouldBe(DelphiLiveOutcomeReasons.MissingMatchingXiu);
        result.ExcessReturn.ReasonCode.ShouldBe(DelphiLiveOutcomeReasons.MissingMatchingXiu);
    }

    [Fact]
    public void LaterReceivedBarCannotMatureAResearchOutcomeBeforeItsReceipt()
    {
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", OpenUtc, 100m, 100m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", OpenUtc, 200m, 200m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC", anchor.EndUtc, 100m, [101m, 102m, 103m, 104m]);
        DelphiLiveFiveMinuteBar endpoint = stock[^1];
        stock[^1] = new(
            endpoint.ObservationId, endpoint.Symbol, endpoint.SessionDate,
            endpoint.StartUtc, endpoint.EndUtc, endpoint.Open, endpoint.High, endpoint.Low,
            endpoint.Close, endpoint.Volume, anchor.EndUtc.AddMinutes(40), "TMX", 1,
            DelphiLiveEvidenceDisposition.LateResearchOnly);
        DelphiLiveOutcomeCalculationInput input = Input(
            anchor, xiuAnchor, stock, [], anchor.EndUtc.AddMinutes(30));

        DelphiLiveOutcomeHorizonResult before = DelphiLiveObservationOutcomeCalculator.Calculate(input, Policy)
            .Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20);
        DelphiLiveOutcomeHorizonResult after = DelphiLiveObservationOutcomeCalculator.Calculate(
                input with { AsOfUtc = anchor.EndUtc.AddMinutes(41) }, Policy)
            .Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20);

        before.RawReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Invalid);
        after.RawReturn.RequireValue().ShouldBe(0.04m);
        after.MaximumFavourableMovement.State.ShouldBe(DelphiLiveOutcomeMetricState.Valid);
    }

    [Fact]
    public void ConflictBeyondAHorizonDoesNotInvalidateItsEarlierCompletePath()
    {
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", OpenUtc, 100m, 100m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", OpenUtc, 200m, 200m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC", anchor.EndUtc, 100m, [101m, 102m, 103m, 104m, 105m]);
        stock.Add(Bar("ABC", stock[^1].StartUtc, 104m, 106m));

        DelphiLiveOutcomeHorizonResult result = DelphiLiveObservationOutcomeCalculator.Calculate(
                Input(anchor, xiuAnchor, stock, [], anchor.EndUtc.AddMinutes(40)), Policy)
            .Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20);

        result.RawReturn.RequireValue().ShouldBe(0.04m);
        result.MaximumFavourableMovement.State.ShouldBe(DelphiLiveOutcomeMetricState.Valid);
        result.OpportunityThresholds.ShouldAllBe(hit => hit.State == DelphiLiveOutcomeMetricState.Valid);
    }

    [Fact]
    public void UnmaturedAndStructurallyImpossibleHorizonsAreDistinct()
    {
        DateTime lateStart = CloseUtc.AddMinutes(-10);
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", lateStart, 100m, 100m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", lateStart, 200m, 200m);

        DelphiLiveObservationOutcome outcome = DelphiLiveObservationOutcomeCalculator.Calculate(
            Input(anchor, xiuAnchor, Array.Empty<DelphiLiveFiveMinuteBar>(), Array.Empty<DelphiLiveFiveMinuteBar>(),
                asOfUtc: anchor.ReceivedUtc.AddSeconds(1)),
            Policy);

        outcome.Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20)
            .RawReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.NotApplicable);
        outcome.Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Session1)
            .RawReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Pending);
        outcome.Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Session3)
            .RawReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Pending);
    }

    [Fact]
    public void SessionThreeCombinesOnlyPostAnchorIntradayPathWithLaterDailySessions()
    {
        DateTime anchorStart = CloseUtc.AddMinutes(-15);
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", anchorStart, 100m, 100m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", anchorStart, 200m, 200m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC", anchor.EndUtc, 100m, new[] { 101m, 102m });
        List<DelphiLiveFiveMinuteBar> xiu = BuildFuture(
            "XIU", xiuAnchor.EndUtc, 200m, new[] { 201m, 202m });
        DateOnly session2 = new(2026, 9, 8);
        DateOnly session3 = new(2026, 9, 9);

        DelphiLiveOutcomeCalculationInput input = Input(anchor, xiuAnchor, stock, xiu, CloseUtc.AddDays(7)) with
        {
            MaturedThroughSession = session3,
            CanonicalSessionDates = new[] { SessionDate, session2, session3, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 11) },
            FutureDailyBars = new[]
            {
                Daily("ABC", SessionDate, 100m, 500m, 1m, 102m), // must never enter the future path
                Daily("ABC", session2, 102m, 106m, 99m, 105m),
                Daily("ABC", session3, 105m, 110m, 95m, 108m)
            },
            FutureXiuDailyBars = new[]
            {
                Daily("XIU", session2, 202m, 204m, 200m, 203m),
                Daily("XIU", session3, 203m, 206m, 201m, 204m)
            }
        };

        DelphiLiveOutcomeHorizonResult result = DelphiLiveObservationOutcomeCalculator.Calculate(input, Policy)
            .Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Session3);

        result.RawReturn.RequireValue().ShouldBe(0.08m);
        result.XiuReturn.RequireValue().ShouldBe(0.02m);
        result.MaximumFavourableMovement.RequireValue().ShouldBe(0.10m);
        result.MaximumAdverseMovement.RequireValue().ShouldBe(-0.05m);
        result.PathOrdering.ShouldBe(DelphiLivePathOrdering.SameSessionUnknown);
        result.OpportunityThresholds.Single(hit => hit.Threshold == 0.05m)
            .FirstSessionOrdinal.ShouldBe(2);
        result.MaximumFavourableMovement.RequireValue().ShouldBeLessThan(4m);
    }

    [Fact]
    public void CorporateActionBlocksMaturedPerformanceEvidence()
    {
        DelphiLiveFiveMinuteBar anchor = Bar("ABC", OpenUtc, 100m, 100m);
        DelphiLiveFiveMinuteBar xiuAnchor = Bar("XIU", OpenUtc, 200m, 200m);
        List<DelphiLiveFiveMinuteBar> stock = BuildFuture(
            "ABC", anchor.EndUtc, 100m, new[] { 101m, 102m, 103m, 104m });
        List<DelphiLiveFiveMinuteBar> xiu = BuildFuture(
            "XIU", xiuAnchor.EndUtc, 200m, new[] { 201m, 202m, 203m, 204m });

        DelphiLiveOutcomeHorizonResult result = DelphiLiveObservationOutcomeCalculator.Calculate(
                Input(anchor, xiuAnchor, stock, xiu, anchor.EndUtc.AddMinutes(30)) with
                {
                    CorporateActionUnsupported = true
                },
                Policy)
            .Horizons.Single(item => item.Horizon == DelphiLiveOutcomeHorizon.Minutes20);

        result.RawReturn.State.ShouldBe(DelphiLiveOutcomeMetricState.Invalid);
        result.RawReturn.ReasonCode.ShouldBe(DelphiLiveOutcomeReasons.CorporateActionUnsupported);
    }

    [Fact]
    public void MetricCoverageKeepsPendingAndInvalidAnchorsInApplicableDenominator()
    {
        DelphiLiveMetricCoverage pending = DelphiLiveCoverageCalculator.Calculate(
            new[]
            {
                DelphiLiveOutcomeMetricState.Valid,
                DelphiLiveOutcomeMetricState.Degraded,
                DelphiLiveOutcomeMetricState.Invalid,
                DelphiLiveOutcomeMetricState.Pending,
                DelphiLiveOutcomeMetricState.NotApplicable
            },
            Policy);

        pending.ApplicableCount.ShouldBe(4);
        pending.CompletionCoverage.ShouldBe(0.75m);
        pending.UsableCoverage.ShouldBe(0.50m);
        pending.Readiness.ShouldBe(DelphiLiveCoverageReadiness.NotMature);

        DelphiLiveMetricCoverage ready = DelphiLiveCoverageCalculator.Calculate(
            Enumerable.Repeat(DelphiLiveOutcomeMetricState.Valid, 20),
            Policy);
        ready.Readiness.ShouldBe(DelphiLiveCoverageReadiness.Ready);

        DelphiLiveMetricCoverage degraded = DelphiLiveCoverageCalculator.Calculate(
            Enumerable.Repeat(DelphiLiveOutcomeMetricState.Valid, 19)
                .Append(DelphiLiveOutcomeMetricState.Invalid),
            Policy);
        degraded.UsableCoverage.ShouldBe(0.95m);
        degraded.Readiness.ShouldBe(DelphiLiveCoverageReadiness.Degraded);
    }

    private static DelphiLiveOutcomeCalculationInput Input(
        DelphiLiveFiveMinuteBar anchor,
        DelphiLiveFiveMinuteBar? xiuAnchor,
        IReadOnlyList<DelphiLiveFiveMinuteBar> stock,
        IReadOnlyList<DelphiLiveFiveMinuteBar> xiu,
        DateTime asOfUtc) =>
        new()
        {
            OutcomeId = Guid.NewGuid(),
            Anchor = anchor,
            XiuAnchor = xiuAnchor,
            SessionCloseUtc = CloseUtc,
            AsOfUtc = asOfUtc,
            MaturedThroughSession = SessionDate,
            CanonicalSessionDates = new[]
            {
                SessionDate,
                new DateOnly(2026, 9, 8),
                new DateOnly(2026, 9, 9),
                new DateOnly(2026, 9, 10),
                new DateOnly(2026, 9, 11)
            },
            FutureIntradayBars = stock,
            FutureXiuIntradayBars = xiu,
            FutureDailyBars = Array.Empty<DelphiLiveDailyBar>(),
            FutureXiuDailyBars = Array.Empty<DelphiLiveDailyBar>(),
            EvidenceBasket = DelphiLiveOutcomeEvidenceBasket.ModelGrade
        };

    private static List<DelphiLiveFiveMinuteBar> BuildFuture(
        string symbol,
        DateTime anchorEndUtc,
        decimal previousClose,
        decimal[] closes)
    {
        var result = new List<DelphiLiveFiveMinuteBar>(closes.Length);
        DateTime start = anchorEndUtc;
        foreach (decimal close in closes)
        {
            result.Add(Bar(symbol, start, previousClose, close));
            previousClose = close;
            start = start.AddMinutes(5);
        }
        return result;
    }

    private static DelphiLiveFiveMinuteBar Bar(
        string symbol,
        DateTime startUtc,
        decimal open,
        decimal close,
        decimal? high = null,
        decimal? low = null) =>
        new(
            Guid.NewGuid(),
            symbol,
            SessionDate,
            startUtc,
            startUtc.AddMinutes(5),
            open,
            high ?? System.Math.Max(open, close) + 0.01m,
            low ?? System.Math.Min(open, close) - 0.01m,
            close,
            100,
            startUtc.AddMinutes(7),
            "TMX",
            1,
            DelphiLiveEvidenceDisposition.OperationalOnTime);

    private static DelphiLiveDailyBar Daily(
        string symbol,
        DateOnly date,
        decimal open,
        decimal high,
        decimal low,
        decimal close) =>
        new(Guid.NewGuid(), symbol, date, open, high, low, close, 1_000);
}
