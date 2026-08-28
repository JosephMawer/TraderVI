#nullable enable

using Core.Trader;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelayedIntradaySwingExitPolicyTests
{
    private static readonly DateTime EntryUtc = new(2026, 8, 25, 13, 35, 0, DateTimeKind.Utc);

    [Fact]
    public void ProfitableCompletedBar_ArmsCostAwareTrailingProtection()
    {
        var state = IntradaySwingPositionState.Open(100m, EntryUtc);

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            state,
            Bar(1, 14, 0, open: 100m, high: 107m, low: 100m, close: 106m));

        decision.Directive.ShouldBe(IntradaySwingDirective.Hold);
        decision.State.ProfitProtectionArmed.ShouldBeTrue();
        decision.State.HighestCompletedClose.ShouldBe(106m);
        decision.State.TrailingStopPrice.ShouldBe(100.70m);
    }

    [Fact]
    public void LaterBarCrossesEstablishedTrail_EmitsExitAlert()
    {
        var first = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            Bar(1, 14, 0, open: 100m, high: 107m, low: 100m, close: 106m));

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            first.State,
            Bar(1, 14, 15, open: 106m, high: 106m, low: 100.50m, close: 101m));

        decision.Directive.ShouldBe(IntradaySwingDirective.ExitAlert);
        decision.Reason.ShouldBe(IntradaySwingReason.TrailingProfit);
        decision.TriggerPrice.ShouldBe(100.70m);
    }

    [Fact]
    public void CurrentBarHigh_DoesNotCreateRetroactiveTrailWithinSameBar()
    {
        var state = IntradaySwingPositionState.Open(100m, EntryUtc);

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            state,
            Bar(1, 14, 0, open: 100m, high: 120m, low: 99m, close: 101m));

        decision.Directive.ShouldBe(IntradaySwingDirective.Hold);
        decision.State.HighestCompletedClose.ShouldBe(101m);
        decision.State.TrailingStopPrice.ShouldBe(
            DelayedIntradaySwingExitPolicy.BreakEvenExitPrice(100m));
    }

    [Fact]
    public void ConditionalLossWithoutFreshEvidence_EmitsExitAlert()
    {
        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            Bar(1, 14, 0, open: 95m, high: 96m, low: 89m, close: 91m));

        decision.Directive.ShouldBe(IntradaySwingDirective.ExitAlert);
        decision.Reason.ShouldBe(IntradaySwingReason.ConditionalLossLimit);
        decision.TriggerPrice.ShouldBe(90m);
    }

    [Fact]
    public void FreshStrongPublishedBreakout_CanBypassConditionalLossAlert()
    {
        var bar = Bar(2, 14, 0, open: 94m, high: 95m, low: 89m, close: 92m);
        var evidence = StrongEvidence(bar.StartUtc.AddMinutes(-5));

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            bar,
            evidence);

        decision.Directive.ShouldBe(IntradaySwingDirective.HoldUnderStrongBreakoutException);
        decision.Reason.ShouldBe(IntradaySwingReason.StrongBreakoutException);
        decision.StrongBreakoutQualified.ShouldBeTrue();
    }

    [Fact]
    public void OriginalRecommendation_CannotBypassConditionalLossAlert()
    {
        var bar = Bar(1, 14, 0, open: 94m, high: 95m, low: 89m, close: 92m);
        var evidence = StrongEvidence(EntryUtc.AddMinutes(-5));

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            bar,
            evidence);

        decision.Directive.ShouldBe(IntradaySwingDirective.ExitAlert);
        decision.Reason.ShouldBe(IntradaySwingReason.ConditionalLossLimit);
    }

    [Fact]
    public void EvidencePersistedAfterPolicyBar_CannotBypassConditionalLossAlert()
    {
        var bar = Bar(2, 14, 0, open: 94m, high: 95m, low: 89m, close: 92m);
        var evidence = StrongEvidence(
            bar.StartUtc.AddMinutes(-5),
            availableUtc: bar.StartUtc.AddSeconds(1));

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            bar,
            evidence);

        decision.Directive.ShouldBe(IntradaySwingDirective.ExitAlert);
        decision.Reason.ShouldBe(IntradaySwingReason.ConditionalLossLimit);
    }

    [Fact]
    public void AbsoluteLossAlwaysEmitsExitAlert()
    {
        var bar = Bar(2, 14, 0, open: 90m, high: 92m, low: 79m, close: 82m);

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            bar,
            StrongEvidence(bar.StartUtc.AddMinutes(-5)));

        decision.Directive.ShouldBe(IntradaySwingDirective.ExitAlert);
        decision.Reason.ShouldBe(IntradaySwingReason.AbsoluteLossLimit);
        decision.TriggerPrice.ShouldBe(80m);
    }

    [Fact]
    public void UnprofitableFifthSessionClose_EmitsTimeExitAlert()
    {
        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            Bar(5, 19, 45, open: 100m, high: 101m, low: 99m, close: 100m, closing: true));

        decision.Directive.ShouldBe(IntradaySwingDirective.ExitAlert);
        decision.Reason.ShouldBe(IntradaySwingReason.FiveSessionUnprofitable);
    }

    [Fact]
    public void ProfitableFifthSessionClose_ContinuesUnderTrail()
    {
        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            Bar(5, 19, 45, open: 104m, high: 106m, low: 103m, close: 105m, closing: true));

        decision.Directive.ShouldBe(IntradaySwingDirective.Hold);
        decision.State.ProfitProtectionArmed.ShouldBeTrue();
    }

    [Fact]
    public void TenthSessionClose_AlwaysEmitsTimeExitAlert()
    {
        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            Bar(10, 19, 45, open: 120m, high: 122m, low: 119m, close: 121m, closing: true));

        decision.Directive.ShouldBe(IntradaySwingDirective.ExitAlert);
        decision.Reason.ShouldBe(IntradaySwingReason.TenSessionMaximum);
    }

    [Fact]
    public void BarReceivedMoreThanFortyFiveMinutesLate_IsFlaggedButStillEvaluated()
    {
        var bar = Bar(1, 14, 0, open: 100m, high: 101m, low: 99m, close: 100m) with
        {
            ReceivedUtc = new DateTime(2026, 8, 25, 15, 1, 0, DateTimeKind.Utc)
        };

        var decision = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            bar);

        decision.IsLate.ShouldBeTrue();
        decision.DataAge.ShouldBe(TimeSpan.FromMinutes(46));
        decision.Directive.ShouldBe(IntradaySwingDirective.Hold);
    }

    [Fact]
    public void DuplicateOrOutOfOrderBar_IsRejected()
    {
        var bar = Bar(1, 14, 0, open: 100m, high: 101m, low: 99m, close: 100m);
        var first = DelayedIntradaySwingExitPolicy.Evaluate(
            IntradaySwingPositionState.Open(100m, EntryUtc),
            bar);

        Should.Throw<ArgumentException>(() =>
            DelayedIntradaySwingExitPolicy.Evaluate(first.State, bar));
    }

    private static DelayedIntradayBreakoutEvidence StrongEvidence(
        DateTime runStartedUtc,
        DateTime? availableUtc = null) =>
        new(
            Guid.Parse("8f17e7a9-cd4c-4cdd-b188-d8cda7b2335a"),
            runStartedUtc,
            availableUtc ?? runStartedUtc.AddSeconds(1),
            IsLatestAvailableOfficialRun: true,
            IsValid: true,
            IsBreakoutPublished: true,
            BreakoutProbability: 0.60,
            DirectionEdge: 0.10,
            DownProbability: 0.349);

    private static DelayedIntradayBar Bar(
        int sessionOrdinal,
        int startHour,
        int startMinute,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        bool closing = false)
    {
        DateTime start = new(2026, 8, 25, startHour, startMinute, 0, DateTimeKind.Utc);
        DateTime end = start.AddMinutes(15);
        return new DelayedIntradayBar(
            start,
            end,
            end.AddMinutes(15),
            sessionOrdinal,
            closing,
            open,
            high,
            low,
            close,
            10_000);
    }
}
