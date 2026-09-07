#nullable enable
using Core.ML;
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Core.Runtime;

public sealed record DailyBenchmarkEvidence(string PolicyVersion, string CalendarVersion,
    DateTime MarketSession, string XiuSource, string SpySource, int XiuObservations, int SpyObservations);

public sealed record DailyBenchmarkSnapshot(IReadOnlyList<DailyBar> Xiu, IReadOnlyList<DailyBar> Spy,
    DailyBenchmarkEvidence Evidence)
{
    public MarketRegime ComputeRegime() => TradeDecisionEngine.ComputeRegime(Xiu, Spy);
}

/// <summary>ADR-0058's explicit daily policy; older strategy decisions retain their prior dispatch.</summary>
public static class DailyBenchmarkPolicy
{
    public const string DecisionRef = "ADR-0058";
    public const string Version = "DailyBenchmarks.XiuSpyObservedV1";
    public const int RequiredObservations = 200;

    public static bool IsRequired(string? decisionRef) => decisionRef == DecisionRef;

    public static ReviewedTsxSessionCalendar LoadCalendar()
    {
        const string variable = "TRADERVI_TSX_CALENDAR_PATH";
        string? path = Environment.GetEnvironmentVariable(variable)
            ?? Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Daily benchmark validation requires the reviewed TSX calendar path.");
        return ReviewedTsxSessionCalendar.Load(path);
    }

    public static DailyBenchmarkSnapshot Prepare(DateTime recommendationDate, ReviewedTsxSessionCalendar calendar,
        IReadOnlyList<DailyBar> xiu, IReadOnlyList<DailyBar> spy)
    {
        DateTime expected = calendar.GetImmediatelyPrecedingSession(DateOnly.FromDateTime(recommendationDate)).ToDateTime(TimeOnly.MinValue);
        var xiuSnapshot = ValidateSeries("XIU", xiu, expected);
        var spySnapshot = ValidateSeries("SPY", spy, expected);
        return new(xiuSnapshot, spySnapshot, new(Version, calendar.Version, expected,
            "DailyBars:XIU (TMX)", "DailyBars:SPY (Yahoo daily chart)", xiuSnapshot.Count, spySnapshot.Count));
    }

    public static IReadOnlyList<DailyBar> ValidateSeries(string symbol, IReadOnlyList<DailyBar> history, DateTime expected)
    {
        // Future observations never enter this recommendation's benchmark calculations.
        var snapshot = history.Where(bar => bar.Date.Date <= expected.Date).OrderBy(bar => bar.Date)
            .Select(bar => new DailyBar { Date = bar.Date.Date, Open = bar.Open, High = bar.High,
                Low = bar.Low, Close = bar.Close, Volume = bar.Volume }).ToArray();
        if (snapshot.Length < RequiredObservations)
            throw new InvalidDataException($"{symbol} confirmation unavailable: need {RequiredObservations} completed observations, found {snapshot.Length}.");
        if (snapshot[^1].Date != expected.Date)
            throw new InvalidDataException($"{symbol} confirmation stale: expected {expected:yyyy-MM-dd}, latest {snapshot[^1].Date:yyyy-MM-dd}. Refresh market data before publishing.");
        DateTime firstRequired = snapshot[^RequiredObservations].Date;
        var required = snapshot.Where(bar => bar.Date >= firstRequired).ToArray();
        if (required.Select(bar => bar.Date).Distinct().Count() != required.Length)
            throw new InvalidDataException($"{symbol} confirmation unavailable: duplicate required session.");
        if (required.Any(bar => !IsValidBar(bar)))
            throw new InvalidDataException($"{symbol} confirmation unavailable: invalid required OHLCV observation.");
        return snapshot;
    }

    public static bool IsValidBar(DailyBar bar) =>
        float.IsFinite(bar.Open) && float.IsFinite(bar.High) && float.IsFinite(bar.Low) && float.IsFinite(bar.Close) &&
        bar.Open > 0 && bar.High > 0 && bar.Low > 0 && bar.Close > 0 && bar.Volume >= 0 &&
        bar.High >= bar.Low && bar.Open <= bar.High && bar.Open >= bar.Low && bar.Close <= bar.High && bar.Close >= bar.Low;
}
