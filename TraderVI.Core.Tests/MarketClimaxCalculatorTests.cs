using System;
using System.Collections.Generic;
using Core.Indicators;
using Core.Indicators.Models;
using Shouldly;
using Xunit;

namespace TraderVI.Core.Tests;

/// <summary>
/// Tests for <see cref="MarketClimaxCalculator"/> — Granville's market-wide Climax (CLX),
/// the net count of OBV (On-Balance Volume) breakouts across the XIU-60 leaders.
///
/// Two surfaces are covered:
///   • <see cref="MarketClimaxCalculator.ClassifyRegime"/> — the windowed confirmation/
///     divergence verdict of CLX vs the XIU benchmark, from hand-built entry lists.
///   • <see cref="MarketClimaxCalculator.ComputeForDate"/> — the per-day tally built from
///     synthetic OBV series (monotonic up/down/flat/short), verifying standing designation,
///     fresh-flow detection, covered counts, and that Indeterminate series are excluded.
/// </summary>
public class MarketClimaxCalculatorTests
{
    private static readonly DateTime Anchor = new(2024, 1, 1);

    private static MarketClimaxEntry Entry(int dayOffset, int clx, float? xiu) => new()
    {
        Date = Anchor.AddDays(dayOffset),
        Clx = clx,
        XiuClose = xiu
    };

    // ── ClassifyRegime ──

    [Fact]
    public void ClassifyRegime_TooFewEntries_ReturnsInsufficient()
    {
        // window 5 needs >= 6 entries; supply only 3.
        var recent = new List<MarketClimaxEntry>
        {
            Entry(0, 5, 20f),
            Entry(1, 6, 21f),
            Entry(2, 7, 22f)
        };

        var result = MarketClimaxCalculator.ClassifyRegime(recent, window: 5, threshold: 3);

        result.Regime.ShouldBe(ClimaxRegime.Insufficient);
    }

    [Fact]
    public void ClassifyRegime_XiuUpAndClxUp_ReturnsConfirming()
    {
        // XIU rises, CLX rises with it → breadth confirms price.
        var recent = new List<MarketClimaxEntry>
        {
            Entry(0, 2, 20.0f),
            Entry(1, 4, 20.5f),
            Entry(2, 6, 21.0f)
        };

        var result = MarketClimaxCalculator.ClassifyRegime(recent, window: 2, threshold: 3);

        result.Regime.ShouldBe(ClimaxRegime.Confirming);
        result.ClxNow.ShouldBe(6);
        result.ClxThen.ShouldBe(2);
        result.ClxChange.ShouldBe(4);
        result.XiuChangePct.ShouldNotBeNull();
        result.XiuChangePct!.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ClassifyRegime_XiuUpButClxFallsHard_ReturnsBearishDivergence()
    {
        // XIU up but CLX collapses by >= threshold → unconfirmed advance.
        var recent = new List<MarketClimaxEntry>
        {
            Entry(0, 8, 20.0f),
            Entry(1, 6, 20.5f),
            Entry(2, 3, 21.0f)   // ΔCLX = 3 - 8 = -5 <= -threshold(3)
        };

        var result = MarketClimaxCalculator.ClassifyRegime(recent, window: 2, threshold: 3);

        result.Regime.ShouldBe(ClimaxRegime.BearishDivergence);
        result.ClxChange.ShouldBe(-5);
    }

    [Fact]
    public void ClassifyRegime_XiuDownButClxRisesHard_ReturnsBullishDivergence()
    {
        // XIU down but CLX climbs by >= threshold → improving breadth.
        var recent = new List<MarketClimaxEntry>
        {
            Entry(0, -4, 21.0f),
            Entry(1, 0, 20.5f),
            Entry(2, 2, 20.0f)   // ΔCLX = 2 - (-4) = +6 >= threshold(3)
        };

        var result = MarketClimaxCalculator.ClassifyRegime(recent, window: 2, threshold: 3);

        result.Regime.ShouldBe(ClimaxRegime.BullishDivergence);
        result.ClxChange.ShouldBe(6);
    }

    [Fact]
    public void ClassifyRegime_SmallMovesNoDivergence_ReturnsNeutral()
    {
        // XIU up, CLX only +1 (< threshold and not a clear same-direction confirm beyond noise).
        var recent = new List<MarketClimaxEntry>
        {
            Entry(0, 5, 20.0f),
            Entry(1, 5, 20.2f),
            Entry(2, 4, 20.5f)   // ΔCLX = -1: not bearish (|−1| < 3), and opposite XIU → neutral
        };

        var result = MarketClimaxCalculator.ClassifyRegime(recent, window: 2, threshold: 3);

        result.Regime.ShouldBe(ClimaxRegime.Neutral);
    }

    [Fact]
    public void ClassifyRegime_NoXiuReference_ReturnsNeutral()
    {
        var recent = new List<MarketClimaxEntry>
        {
            Entry(0, 5, null),
            Entry(1, 7, null),
            Entry(2, 9, null)
        };

        var result = MarketClimaxCalculator.ClassifyRegime(recent, window: 2, threshold: 3);

        result.Regime.ShouldBe(ClimaxRegime.Neutral);
        result.XiuChangePct.ShouldBeNull();
    }

    // ── ComputeForDate ──

    /// <summary>Builds an OBV series with the given closing values, one per consecutive day.</summary>
    private static List<OBV> Series(params long[] values)
    {
        var list = new List<OBV>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            long delta = i == 0 ? 0 : values[i] - values[i - 1];
            list.Add(new OBV(Anchor.AddDays(i), values[i], delta));
        }
        return list;
    }

    [Fact]
    public void ComputeForDate_MonotonicUp_CountsUpAndFresh()
    {
        // Strictly rising OBV → newest point makes a new high vs the prior 3 → UP, fired today.
        var series = Series(1, 2, 3, 4, 5, 6, 7, 8);
        var asOf = Anchor.AddDays(7); // last point's date

        var bySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAA"] = series
        };

        var entry = MarketClimaxCalculator.ComputeForDate(bySymbol, asOf, breakoutWindow: 3, xiuClose: 25f);

        entry.UpBreakouts.ShouldBe(1);
        entry.DownBreakouts.ShouldBe(0);
        entry.Clx.ShouldBe(1);
        entry.FreshUp.ShouldBe(1);
        entry.FreshDown.ShouldBe(0);
        entry.Covered.ShouldBe(1);
        entry.BasketSize.ShouldBe(1);
        entry.XiuClose.ShouldBe(25f);
        entry.Date.ShouldBe(asOf.Date);
    }

    [Fact]
    public void ComputeForDate_MonotonicDown_CountsDown()
    {
        // Strictly falling OBV → newest point makes a new low → DOWN designation.
        var series = Series(8, 7, 6, 5, 4, 3, 2, 1);
        var asOf = Anchor.AddDays(7);

        var bySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase)
        {
            ["BBB"] = series
        };

        var entry = MarketClimaxCalculator.ComputeForDate(bySymbol, asOf, breakoutWindow: 3, xiuClose: null);

        entry.DownBreakouts.ShouldBe(1);
        entry.UpBreakouts.ShouldBe(0);
        entry.Clx.ShouldBe(-1);
        entry.FreshDown.ShouldBe(1);
        entry.Covered.ShouldBe(1);
        entry.BasketSize.ShouldBe(1);
    }

    [Fact]
    public void ComputeForDate_Flat_ExcludedFromCovered_ButCounted()
    {
        // Flat OBV → no breakout ever fires → Doubtful/None designation. It still has enough
        // history to be classifiable (not Indeterminate), so it counts toward BasketSize but
        // not toward Covered (no UP/DOWN).
        var series = Series(5, 5, 5, 5, 5, 5, 5, 5);
        var asOf = Anchor.AddDays(7);

        var bySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CCC"] = series
        };

        var entry = MarketClimaxCalculator.ComputeForDate(bySymbol, asOf, breakoutWindow: 3, xiuClose: null);

        entry.UpBreakouts.ShouldBe(0);
        entry.DownBreakouts.ShouldBe(0);
        entry.Covered.ShouldBe(0);
        entry.BasketSize.ShouldBe(1); // classifiable, just no directional designation
    }

    [Fact]
    public void ComputeForDate_TooShortSeries_ExcludedFromBasket()
    {
        // Only 2 points but window 3 needs >= 4 → Indeterminate → excluded from BasketSize.
        var series = Series(1, 2);
        var asOf = Anchor.AddDays(1);

        var bySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase)
        {
            ["DDD"] = series
        };

        var entry = MarketClimaxCalculator.ComputeForDate(bySymbol, asOf, breakoutWindow: 3, xiuClose: null);

        entry.BasketSize.ShouldBe(0);
        entry.Covered.ShouldBe(0);
        entry.Clx.ShouldBe(0);
    }

    [Fact]
    public void ComputeForDate_MixedBasket_NetsUpMinusDown()
    {
        // Two up names, one down name, one flat, one too-short.
        var bySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase)
        {
            ["UP1"] = Series(1, 2, 3, 4, 5, 6, 7, 8),
            ["UP2"] = Series(2, 3, 4, 5, 6, 7, 8, 9),
            ["DN1"] = Series(8, 7, 6, 5, 4, 3, 2, 1),
            ["FLAT"] = Series(5, 5, 5, 5, 5, 5, 5, 5),
            ["SHORT"] = Series(1, 2)
        };
        var asOf = Anchor.AddDays(7);

        var entry = MarketClimaxCalculator.ComputeForDate(bySymbol, asOf, breakoutWindow: 3, xiuClose: 30f);

        entry.UpBreakouts.ShouldBe(2);
        entry.DownBreakouts.ShouldBe(1);
        entry.Clx.ShouldBe(1);
        entry.Covered.ShouldBe(3);
        entry.BasketSize.ShouldBe(4); // UP1, UP2, DN1, FLAT (SHORT is Indeterminate)
    }

    [Fact]
    public void ComputeForDate_TruncatesSeriesToAsOf_NoFreshWhenBreakoutBeforeAsOf()
    {
        // Rising series ends day 7, but we evaluate as-of day 9 (a later session with no new
        // OBV points). The latest UP breakout fired on day 7, not day 9 → counted as standing
        // UP but NOT fresh.
        var series = Series(1, 2, 3, 4, 5, 6, 7, 8); // last date = Anchor+7
        var asOf = Anchor.AddDays(9);

        var bySymbol = new Dictionary<string, IReadOnlyList<OBV>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAA"] = series
        };

        var entry = MarketClimaxCalculator.ComputeForDate(bySymbol, asOf, breakoutWindow: 3, xiuClose: null);

        entry.UpBreakouts.ShouldBe(1);
        entry.FreshUp.ShouldBe(0); // breakout fired Anchor+7, asOf is Anchor+9
        entry.Date.ShouldBe(asOf.Date);
    }
}
