#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Bind immutable local books to a reviewed daily source; no Paper decisions or network reads.</summary>
public static class CollectorLocalEvidence
{
    public static ImmutableArray<TradingMinuteObservation> Bind(CompleteTradingStrategy strategy,
        PaperComparisonAccount comparison, PaperComparisonSource source, Guid expectedRun,
        DateTime open, DateTime close, DateTime now, IReadOnlyDictionary<string, DelphiLiveMinuteEvidenceBook> books)
    {
        strategy.Validate(); var minute = strategy.MinuteEntry ?? throw new ArgumentException("Explicit minute strategy required.");
        if (comparison.StrategyCode != "DelphiLive" || source.RunId != expectedRun ||
            source.DailyStrategyVersionId != strategy.Daily.StrategyVersionId || source.BatchId != comparison.BatchId ||
            source.PublishedUtc > now || source.AdoptedUtc > now || source.EffectiveUtc > now || comparison.EffectiveUtc > now ||
            close <= open || now < open || now > close.AddMinutes(45))
            throw new ArgumentException("Local minute inputs require the current prospectively adopted daily source.");
        var admission = PaperGateComparison.Admission(comparison with { LivePolicyVersionId = strategy.PolicyVersionId }, source, strategy.PolicyVersionId);
        admission.Validate(expectedRun, strategy.Daily.StrategyVersionId, strategy.PolicyVersionId, now);
        DelphiLiveMinuteEvidenceBook Book(string symbol)
        {
            if (!books.TryGetValue(symbol, out var value)) return new(symbol, minute.SourceContract, open, close, [], []);
            DelphiLiveMinuteEvidence.Validate(value);
            if (value.Symbol != symbol || value.SourceContract != minute.SourceContract || value.SessionOpenUtc != open || value.SessionCloseUtc != close)
                throw new ArgumentException("Collector book identity differs from the reviewed source/session.");
            return value;
        }
        var benchmark = Book("XIU");
        var result = ImmutableArray.CreateBuilder<TradingMinuteObservation>();
        foreach (var candidate in PaperGateComparison.SelectLive(source, comparison.Profile).Values.OrderBy(c => c.Symbol, StringComparer.Ordinal))
        {
            var setup = new DelphiLiveDailySetupQuality(source.RunId, candidate.CandidateId, source.DailyStrategyVersionId,
                candidate.CommonComposite, candidate.SourceLenses.Select(l => new DelphiLiveSourceLensQuality(
                    Enum.Parse<DelphiLiveSourceLens>(l.Lens), l.Eligible, l.Published, l.Rank, l.RankingKey,
                    l.FirstFailure ?? "Published", l.GateTraceJson)).ToImmutableArray()) { ComparisonAdmission = admission };
            var baseline = source.Baselines.GetValueOrDefault(candidate.Symbol);
            // Missing baseline keeps the candidate visible but cannot manufacture a volatility ruler.
            var rulers = baseline?.Rulers ?? DelphiLiveMeasurements.CalculateVolatilityRulers([], [],
                DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(open)), minute.Rules);
            result.Add(new(Book(candidate.Symbol), benchmark, rulers, baseline?.PreviousClose,
                source.Baselines.GetValueOrDefault("XIU")?.PreviousClose, setup));
        }
        return result.ToImmutable();
    }

    public static ImmutableArray<TradingPriceBar> ProtectionBars(DelphiLiveMinuteEvidenceBook book, DateTime now)
    {
        DelphiLiveMinuteEvidence.Validate(book);
        var result = ImmutableArray.CreateBuilder<TradingPriceBar>();
        for (var end = book.SessionOpenUtc.AddMinutes(5); end <= book.SessionCloseUtc && end < now; end = end.AddMinutes(5))
        {
            var window = DelphiLiveMinuteWindows.BuildRecoveredContext(book, end, TimeSpan.FromMinutes(5), now);
            if (window.Aggregate is not { } bar) continue;
            var inputs = book.Observations.Where(o => o.EndUtc > end.AddMinutes(-5) && o.EndUtc <= end).ToArray();
            var id = BrokerManagedStrategy.StableId("CollectorFiveMinuteCloseV1:" + string.Join(":", inputs.Select(o => o.ObservationId.ToString("D"))));
            result.Add(new(id, end.AddMinutes(-5), inputs.Max(o => o.CheckedUtc), 5, bar.Open, bar.High, bar.Low, bar.Close));
        }
        return result.ToImmutable();
    }
}
