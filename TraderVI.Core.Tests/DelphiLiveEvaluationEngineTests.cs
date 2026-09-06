#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveEvaluationEngineTests
{
    private static readonly DateOnly SessionDate = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void OpeningPipelineNeedsFourBarsThenFreshConfirmationAndPreservesAllEvidence()
    {
        DelphiLiveEvaluationInput three = Input(3);
        DelphiLiveEvaluationResult warming = DelphiLiveEvaluationEngine.Evaluate(three);
        DelphiLiveEvaluationInput four = Input(4) with { PreviousState = warming.NextState };
        DelphiLiveEvaluationResult emerging = DelphiLiveEvaluationEngine.Evaluate(four);
        DelphiLiveEvaluationInput five = Input(5) with { PreviousState = emerging.NextState };
        DelphiLiveEvaluationResult confirmed = DelphiLiveEvaluationEngine.Evaluate(five);

        warming.NextState.Confidence.ShouldBe(DelphiLiveDataConfidence.Normal);
        warming.FamiliesMature.ShouldBeFalse();
        warming.Lifecycle.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.WarmingUp);
        emerging.FamiliesMature.ShouldBeTrue();
        emerging.Lifecycle.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.Emerging);
        confirmed.Lifecycle.MayCreateBuyDecision.ShouldBeTrue();
        confirmed.ConfirmedLiveEligible.ShouldBeTrue();
        confirmed.NextState.Momentum.StrongTier.ShouldBe(DelphiLiveStrongTier.FourOfFour);
        confirmed.Counterfactuals.Length.ShouldBe(5);
        confirmed.RawValues["PreviousCloseXiuReturn"].ShouldBe(0m);
        confirmed.RawValues["PreviousCloseExcessReturn"].ShouldBe(0.05m);
        confirmed.DerivedFacts["FrozenRulerSourceThrough"].ShouldBe("2026-09-04");
        confirmed.RawValues["FullDayVolumeFraction20"].ShouldBe(0.05m);
    }

    [Fact]
    public void MissingPairFreezesJudgmentBreaksConfirmationAndOneCleanPairRestoresConfidence()
    {
        DelphiLiveEvaluationResult first = DelphiLiveEvaluationEngine.Evaluate(Input(4));
        DelphiLiveEvaluationInput missedInput = Input(5) with
        {
            PreviousState = first.NextState,
            ExactPairPersistedOnTime = false,
            Xiu = Series("XIU", 4, false)
        };
        DelphiLiveEvaluationResult missed = DelphiLiveEvaluationEngine.Evaluate(missedInput);
        DelphiLiveEvaluationInput freshInput = Input(6) with
        {
            PreviousState = missed.NextState,
            Stock = Series("ABC", 6, true, Open.AddMinutes(30)),
            Xiu = Series("XIU", 6, false, Open.AddMinutes(30))
        };
        DelphiLiveEvaluationResult fresh = DelphiLiveEvaluationEngine.Evaluate(freshInput);

        missed.NextState.Momentum.ShouldBe(first.NextState.Momentum);
        missed.NextState.FamilyJudgments.ShouldBe(first.NextState.FamilyJudgments);
        missed.NextState.Confidence.State.ShouldBe(DelphiLiveDataConfidenceState.Ambiguous);
        missed.NextState.Lifecycle.ConsecutiveStrongObservations.ShouldBe(0);
        missed.ConfirmedLiveEligible.ShouldBeFalse();
        fresh.NextState.Confidence.ShouldBe(DelphiLiveDataConfidence.Normal);
        fresh.FamiliesMature.ShouldBeFalse();
        fresh.Lifecycle.MayCreateBuyDecision.ShouldBeFalse();
    }

    [Fact]
    public void ResearchConfirmationIgnoresOwnedPositionsAndPendingActions()
    {
        DelphiLiveEvaluationInput first = Input(4) with { IsHeld = true, AveragePurchasePrice = 100m };
        DelphiLiveEvaluationResult emerging = DelphiLiveEvaluationEngine.Evaluate(first);
        DelphiLiveEvaluationInput second = Input(5) with
        {
            PreviousState = emerging.NextState,
            IsHeld = true,
            AveragePurchasePrice = 100m,
            HasPendingSell = true
        };

        DelphiLiveEvaluationResult held = DelphiLiveEvaluationEngine.Evaluate(second);

        held.Lifecycle.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.ExitPending);
        held.Lifecycle.MayCreateBuyDecision.ShouldBeFalse();
        held.ConfirmedLiveEligible.ShouldBeTrue();
    }

    [Fact]
    public void RestartRequiresFiveFreshBarsForMaturityAndSixForConfirmation()
    {
        DateTime firstFreshEnd = Open.AddMinutes(65);
        DelphiLiveEvaluationResult? prior = null;
        for (int freshCount = 1; freshCount <= 6; freshCount++)
        {
            int barCount = 12 + freshCount;
            DelphiLiveEvaluationInput input = Input(barCount) with
            {
                PreviousState = prior?.NextState ?? DelphiLiveEvaluationState.Initial(true),
                Stock = Series("ABC", barCount, true, firstFreshEnd),
                Xiu = Series("XIU", barCount, false, firstFreshEnd)
            };
            DelphiLiveEvaluationResult result = DelphiLiveEvaluationEngine.Evaluate(input);

            result.FamiliesMature.ShouldBe(freshCount >= 5);
            result.Lifecycle.MayCreateBuyDecision.ShouldBe(freshCount == 6);
            prior = result;
        }
    }

    private static DelphiLiveEvaluationInput Input(int bars) => new()
    {
        EvaluationId = Guid.NewGuid(),
        SessionId = Guid.NewGuid(),
        BarEndUtc = Open.AddMinutes(bars * 5),
        EvaluatedUtc = Open.AddMinutes(bars * 5 + 2),
        Stock = Series("ABC", bars, true),
        Xiu = Series("XIU", bars, false),
        VolatilityRulers = new(Ruler(5), Ruler(10), Ruler(14), Ruler(20)),
        PreviousState = DelphiLiveEvaluationState.Initial(true),
        Policy = DelphiLivePolicyDefinition.Version1,
        PreviousStockSessionClose = 100m,
        PreviousXiuSessionClose = 100m,
        MedianFullDayVolume20 = 10_000m,
        ExactPairPersistedOnTime = true,
        DailySetup = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0.5m,
            ImmutableArray.Create(new DelphiLiveSourceLensQuality(
                DelphiLiveSourceLens.Continuation, true, true, 1, 0.5m, "DailyReasons", "DailyGates")))
    };

    private static DelphiLiveFiveMinuteSeries Series(string symbol, int count, bool rising, DateTime? continuity = null) =>
        new(symbol, SessionDate, Open, continuity ?? Open,
            Enumerable.Range(0, count).Select(index => new DelphiLiveFiveMinuteBar(
                Guid.NewGuid(), symbol, SessionDate, Open.AddMinutes(5 * index), Open.AddMinutes(5 * (index + 1)),
                100m + (rising ? index : 0), 100.01m + (rising ? index + 1 : 0),
                99.99m + (rising ? index : 0), 100m + (rising ? index + 1 : 0),
                100, Open.AddMinutes(5 * (index + 1) + 2), "TMX", 1,
                DelphiLiveEvidenceDisposition.OperationalOnTime)));

    private static DelphiLiveTrueRangeRulerMeasurement Ruler(int sessions) =>
        new(sessions, new DateOnly(2026, 9, 4), DelphiLiveScalarMeasurement.Available(0.04m));
}
