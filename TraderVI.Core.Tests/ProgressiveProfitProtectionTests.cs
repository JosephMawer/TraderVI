#nullable enable
using Core.Trader;
using Shouldly;
using System;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ProgressiveProfitProtectionTests
{
    private static readonly DateTime At = new(2026, 10, 16, 14, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData("101.49", null)]
    [InlineData("101.50", "100.485")]
    [InlineData("103", "102.2275")]
    [InlineData("105", "104.475")]
    public void BandsActivateAtExactCompletedClose(string close, string? expected)
    {
        var state = ProgressiveProfitProtection.ObserveClose(new(100m), 100m, At, decimal.Parse(close, System.Globalization.CultureInfo.InvariantCulture), At.AddSeconds(1));
        state.StopPrice.ShouldBe(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AddOnAndRestartPreserveEarnedGapHighAndAbsoluteStop()
    {
        var state = ProgressiveProfitProtection.Carry(100m, null, At, 105m);
        state = JsonSerializer.Deserialize<ProgressiveProfitProtectionState>(JsonSerializer.Serialize(state))!;
        state = ProgressiveProfitProtection.ObserveClose(state, 101m, At.AddDays(1), 104m, At.AddDays(1).AddSeconds(1));
        state.TightestGap.ShouldBe(0.005m);
        state.HighestClose.ShouldBe(105m);
        state.StopPrice.ShouldBe(104.475m);
        state.AverageCost.ShouldBe(101m);
    }

    [Fact]
    public void ExistingStrongerStopAndDuplicateBarNeverLoosenProtection()
    {
        var state = ProgressiveProfitProtection.Carry(100m, 106m, At);
        state = ProgressiveProfitProtection.ObserveClose(state, 100m, At, 105m, At.AddSeconds(1));
        state.StopPrice.ShouldBe(106m);
        ProgressiveProfitProtection.ObserveClose(state, 100m, At, 110m, At.AddSeconds(2)).HighestClose.ShouldBe(105m);
    }

    [Fact]
    public void BidTouchDoesNotWaitForCloseAndUsesAdaptiveDistance()
    {
        var state = ProgressiveProfitProtection.Carry(99m, 100m, At);
        var far = ProgressiveProfitProtection.ObserveBid(state, 100.80m, At, At.AddSeconds(1), At.AddSeconds(1));
        far.State.NextBidCheckUtc.ShouldBe(At.AddMinutes(5).AddSeconds(1));
        var near = ProgressiveProfitProtection.ObserveBid(far.State, 100.25m, At.AddMinutes(5), At.AddMinutes(5).AddSeconds(1), At.AddMinutes(5).AddSeconds(1));
        near.State.NextBidCheckUtc.ShouldBe(At.AddMinutes(6).AddSeconds(1));
        ProgressiveProfitProtection.ObserveBid(near.State, 100m, At.AddMinutes(6), At.AddMinutes(6).AddSeconds(1), At.AddMinutes(6).AddSeconds(1)).Breached.ShouldBeTrue();
    }

    [Fact]
    public void DurableTransitionRejectsLosingEarnedProtectionOrObservedExit()
    {
        var state = ProgressiveProfitProtection.Carry(100m, null, At, 105m);
        state = ProgressiveProfitProtection.ObserveBid(state, 104m, At, At.AddSeconds(1), At.AddSeconds(1)).State;
        Should.Throw<InvalidOperationException>(() => ProgressiveProfitProtection.RequirePreserved(state, null));
        Should.Throw<InvalidOperationException>(() => ProgressiveProfitProtection.RequirePreserved(state, state with { TightestGap = 0.01m }));
        Should.Throw<InvalidOperationException>(() => ProgressiveProfitProtection.RequirePreserved(state, state with { StopPrice = 103m }));
        Should.Throw<InvalidOperationException>(() => ProgressiveProfitProtection.RequirePreserved(state, state with { ExitTriggered = false }));
        ProgressiveProfitProtection.RequirePreserved(state, JsonSerializer.Deserialize<ProgressiveProfitProtectionState>(JsonSerializer.Serialize(state))!);
    }

    [Fact]
    public void QuotesBeforeFloorInvalidMissingOrTooOldCannotTrigger()
    {
        var state = ProgressiveProfitProtection.Carry(99m, 100m, At);
        ProgressiveProfitProtection.ObserveBid(state, 99m, At.AddSeconds(-1), At.AddSeconds(1), At.AddSeconds(1)).Usable.ShouldBeFalse();
        ProgressiveProfitProtection.ObserveBid(state, null, At, At.AddSeconds(1), At.AddSeconds(1)).Breached.ShouldBeFalse();
        ProgressiveProfitProtection.ObserveBid(state, 99m, At, At.AddSeconds(1), At.AddSeconds(62)).Usable.ShouldBeFalse();
        ProgressiveProfitProtection.ObserveBid(state, 99m, At, At.AddSeconds(1), At.AddSeconds(61)).Breached.ShouldBeTrue();
    }
}
