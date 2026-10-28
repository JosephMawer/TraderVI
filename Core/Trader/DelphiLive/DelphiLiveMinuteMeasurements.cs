#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveMinuteMeasurementsResult(DelphiLivePersistenceMeasurements Persistence,
    DelphiLivePriceMovementMeasurements Price, DelphiLiveDirectionalVolumeMeasurements Volume,
    DelphiLivePriceStructureMeasurements Structure, DateTime VwapThroughUtc);

/// <summary>
/// Minute endpoints with unchanged elapsed horizons and four five-minute persistence/volume legs.
/// Session VWAP retains opening-anchored completed five-minute typical-price/volume buckets.
/// No minute aggregate is relabelled as an operational V1 bar or as on-time historical evidence.
/// </summary>
public static class DelphiLiveMinuteMeasurements
{
    public static DelphiLiveMinuteMeasurementsResult Calculate(DelphiLiveMinuteEvidenceBook stock,
        DelphiLiveMinuteEvidenceBook xiu, DateTime end, DateTime now, decimal? previousStockClose,
        decimal? previousXiuClose, DelphiLivePolicyDefinition rules)
    {
        rules.Validate(); DelphiLiveMinuteEvidence.Validate(stock); DelphiLiveMinuteEvidence.Validate(xiu);
        if (xiu.Symbol != "XIU" || stock.SourceContract != xiu.SourceContract || stock.SessionOpenUtc != xiu.SessionOpenUtc ||
            stock.SessionCloseUtc != xiu.SessionCloseUtc) throw new ArgumentException("Matched stock/benchmark session required.");
        var s = new Reader(stock, now); var x = new Reader(xiu, now);
        var stockLegs = s.Legs(end, rules.PersistenceObservationCount);
        var xiuLegs = x.Legs(end, rules.PersistenceObservationCount);
        var availability = Combine(stockLegs.Status, xiuLegs.Status);
        var comparisons = ImmutableArray<DelphiLiveIntervalComparison>.Empty;
        if (availability == DelphiLiveMeasurementAvailability.Available)
            comparisons = stockLegs.Values.Zip(xiuLegs.Values, (a, b) => new DelphiLiveIntervalComparison(a.End,
                Return(a.Start, a.Close), Return(b.Start, b.Close),
                a.Close > a.Start && Return(a.Start, a.Close) > Return(b.Start, b.Close) ? 1 :
                a.Close < a.Start && Return(a.Start, a.Close) < Return(b.Start, b.Close) ? -1 : 0)).ToImmutableArray();
        var persistence = new DelphiLivePersistenceMeasurements(availability, comparisons,
            availability == DelphiLiveMeasurementAvailability.Available ? comparisons.Sum(c => c.Contribution) : null, availability.ToString());
        DelphiLiveWindowReturnMeasurement Pair(TimeSpan horizon)
        {
            var a = s.RollingReturn(end, horizon); var b = x.RollingReturn(end, horizon);
            return new(horizon, a, b, Difference(a, b));
        }
        var priorStock = PriorReturn(s.Close(end), previousStockClose);
        var priorXiu = PriorReturn(x.Close(end), previousXiuClose);
        var price = new DelphiLivePriceMovementMeasurements(end, Pair(rules.ImmediateMovementHorizon), Pair(rules.SustainedMovementHorizon),
            Pair(rules.TwoHourContextHorizon), Pair(rules.ThreeHourContextHorizon), priorStock)
        { PreviousCloseBenchmarkReturn = priorXiu, PreviousCloseExcessReturn = Difference(priorStock, priorXiu) };

        var volumeLegs = s.Legs(end, rules.DirectionalVolumeObservationCount);
        DelphiLiveDirectionalVolumeMeasurements volume;
        if (volumeLegs.Status != DelphiLiveMeasurementAvailability.Available)
            volume = new(end, Missing(volumeLegs.Status), Missing(volumeLegs.Status), null);
        else
        {
            long total = 0; decimal weighted = 0;
            foreach (var leg in volumeLegs.Values) { total = checked(total + leg.Volume); weighted += System.Math.Sign(leg.Close - leg.Start) * (decimal)leg.Volume; }
            volume = new(end, total > 0 ? Available(weighted / total) : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.ZeroTotalVolume),
                Available(Return(volumeLegs.Values[0].Start, volumeLegs.Values[^1].Close)), total);
        }

        // The reference excludes the current five-minute leg, exactly as the frozen measurement.
        var rangeEnd = end.AddMinutes(-5);
        var range = rangeEnd <= stock.SessionOpenUtc ? null : s.Window(rangeEnd, TimeSpan.FromMinutes(5 * rules.PriorRangeObservationCount));
        var rangeStatus = range is null ? DelphiLiveMeasurementAvailability.NotMature : Status(range.Status);
        var priorRange = new DelphiLivePriorRangeMeasurements(rangeStatus, range?.Aggregate?.High, range?.Aggregate?.Low, rangeStatus.ToString());
        var vwapThrough = stock.SessionOpenUtc.AddTicks((end - stock.SessionOpenUtc).Ticks / TimeSpan.FromMinutes(5).Ticks * TimeSpan.FromMinutes(5).Ticks);
        var structure = new DelphiLivePriceStructureMeasurements(end, s.Close(end), previousStockClose is > 0 ? Available(previousStockClose.Value) : Missing(DelphiLiveMeasurementAvailability.Unavailable),
            s.SessionVwap(end, vwapThrough), priorRange);
        return new(persistence, price, volume, structure, vwapThrough);
    }

    private sealed record Leg(DateTime End, decimal Start, decimal Close, long Volume);
    private sealed record LegsResult(DelphiLiveMeasurementAvailability Status, ImmutableArray<Leg> Values);
    private sealed class Reader(DelphiLiveMinuteEvidenceBook book, DateTime now)
    {
        public DelphiLiveMinuteWindow Window(DateTime end, TimeSpan duration) => DelphiLiveMinuteWindows.BuildRecoveredContext(book, end, duration, now);
        public DelphiLiveScalarMeasurement Close(DateTime end)
        {
            if (end == book.SessionOpenUtc)
            {
                var first = Window(end.AddMinutes(1), TimeSpan.FromMinutes(1));
                return first.Aggregate is null ? Missing(Status(first.Status)) : Available(first.Aggregate.Open);
            }
            var window = Window(end, TimeSpan.FromMinutes(1));
            return window.Aggregate is null ? Missing(Status(window.Status)) : Available(window.Aggregate.Close);
        }
        public DelphiLiveScalarMeasurement RollingReturn(DateTime end, TimeSpan duration)
        {
            var window = Window(end, duration);
            if (window.Aggregate is null) return Missing(Status(window.Status));
            var start = Close(end - duration);
            return start.Value is decimal value ? Available(Return(value, window.Aggregate.Close)) : start;
        }
        public LegsResult Legs(DateTime end, int count)
        {
            var window = Window(end, TimeSpan.FromMinutes(5 * count));
            if (window.Aggregate is null) return new(Status(window.Status), []);
            var initial = Close(window.StartUtc);
            if (initial.Value is not decimal prior) return new(initial.Availability, []);
            var result = ImmutableArray.CreateBuilder<Leg>();
            for (int i = 0; i < count; i++)
            {
                var legEnd = window.StartUtc.AddMinutes((i + 1) * 5);
                var leg = Window(legEnd, TimeSpan.FromMinutes(5)).Aggregate!;
                result.Add(new(legEnd, prior, leg.Close, leg.Volume)); prior = leg.Close;
            }
            return new(DelphiLiveMeasurementAvailability.Available, result.ToImmutable());
        }
        public DelphiLiveScalarMeasurement SessionVwap(DateTime end, DateTime through)
        {
            // Even when the reference last updated at a five-minute boundary, missing minute context
            // anywhere from the opening bell stays unavailable. No synthetic/no-trade intervals.
            var path = Window(end, end - book.SessionOpenUtc);
            if (path.Aggregate is null) return Missing(Status(path.Status));
            if (through == book.SessionOpenUtc) return Missing(DelphiLiveMeasurementAvailability.NotMature);
            decimal volume = 0, weighted = 0;
            for (var e = book.SessionOpenUtc.AddMinutes(5); e <= through; e = e.AddMinutes(5))
            {
                var bar = Window(e, TimeSpan.FromMinutes(5)).Aggregate!;
                volume += bar.Volume; weighted += (bar.High + bar.Low + bar.Close) / 3m * bar.Volume;
            }
            return volume > 0 ? Available(weighted / volume) : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.ZeroTotalVolume);
        }
    }
    private static decimal Return(decimal start, decimal end) => end / start - 1m;
    private static DelphiLiveScalarMeasurement Available(decimal value) => DelphiLiveScalarMeasurement.Available(value);
    private static DelphiLiveScalarMeasurement Missing(DelphiLiveMeasurementAvailability status) => status == DelphiLiveMeasurementAvailability.NotMature
        ? DelphiLiveScalarMeasurement.NotMature() : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.MissingContiguousPath);
    private static DelphiLiveMeasurementAvailability Status(DelphiLiveMinuteWindowStatus status) => status switch
    { DelphiLiveMinuteWindowStatus.Available => DelphiLiveMeasurementAvailability.Available, DelphiLiveMinuteWindowStatus.NotMature => DelphiLiveMeasurementAvailability.NotMature, _ => DelphiLiveMeasurementAvailability.Unavailable };
    private static DelphiLiveMeasurementAvailability Combine(DelphiLiveMeasurementAvailability a, DelphiLiveMeasurementAvailability b) =>
        a == DelphiLiveMeasurementAvailability.Unavailable || b == DelphiLiveMeasurementAvailability.Unavailable ? DelphiLiveMeasurementAvailability.Unavailable :
        a == DelphiLiveMeasurementAvailability.NotMature || b == DelphiLiveMeasurementAvailability.NotMature ? DelphiLiveMeasurementAvailability.NotMature : DelphiLiveMeasurementAvailability.Available;
    private static DelphiLiveScalarMeasurement Difference(DelphiLiveScalarMeasurement a, DelphiLiveScalarMeasurement b) =>
        a.Value is decimal av && b.Value is decimal bv ? Available(av - bv) : Missing(Combine(a.Availability, b.Availability));
    private static DelphiLiveScalarMeasurement PriorReturn(DelphiLiveScalarMeasurement close, decimal? prior) =>
        close.Value is decimal price && prior is > 0 ? Available(Return(prior.Value, price)) : Missing(DelphiLiveMeasurementAvailability.Unavailable);
}
