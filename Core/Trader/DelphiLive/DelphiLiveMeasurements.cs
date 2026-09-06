#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

/// <summary>
/// Pure causal measurements. This stage applies no recommendation thresholds.
/// </summary>
public static class DelphiLiveMeasurements
{
    public static DelphiLivePersistenceMeasurements CalculatePersistence(
        DelphiLiveFiveMinuteSeries stock,
        DelphiLiveFiveMinuteSeries benchmark,
        DateTime currentBarEndUtc,
        DelphiLivePolicyDefinition policy)
    {
        ValidateInputs(stock, benchmark, currentBarEndUtc, policy);

        RollingIntervals stockIntervals = BuildRollingIntervals(
            stock,
            currentBarEndUtc,
            policy.PersistenceObservationCount,
            policy.BarInterval);
        RollingIntervals benchmarkIntervals = BuildRollingIntervals(
            benchmark,
            currentBarEndUtc,
            policy.PersistenceObservationCount,
            policy.BarInterval);

        DelphiLiveMeasurementAvailability availability = CombineAvailability(
            stockIntervals.Availability,
            benchmarkIntervals.Availability);
        if (availability != DelphiLiveMeasurementAvailability.Available)
        {
            return new DelphiLivePersistenceMeasurements(
                availability,
                ImmutableArray<DelphiLiveIntervalComparison>.Empty,
                null,
                FirstUnavailableReason(stockIntervals, benchmarkIntervals));
        }

        var comparisons = ImmutableArray.CreateBuilder<DelphiLiveIntervalComparison>(
            policy.PersistenceObservationCount);
        int score = 0;
        for (int index = 0; index < stockIntervals.Legs.Length; index++)
        {
            RollingIntervalLeg stockLeg = stockIntervals.Legs[index];
            RollingIntervalLeg benchmarkLeg = benchmarkIntervals.Legs[index];
            if (stockLeg.EndUtc != benchmarkLeg.EndUtc)
            {
                return new DelphiLivePersistenceMeasurements(
                    DelphiLiveMeasurementAvailability.Unavailable,
                    ImmutableArray<DelphiLiveIntervalComparison>.Empty,
                    null,
                    DelphiLiveReasonCodes.MissingExactEndpoint);
            }

            decimal stockReturn = Return(stockLeg.StartPrice, stockLeg.EndPrice);
            decimal benchmarkReturn = Return(benchmarkLeg.StartPrice, benchmarkLeg.EndPrice);
            int contribution = stockReturn > 0m && stockReturn > benchmarkReturn
                ? 1
                : stockReturn < 0m && stockReturn < benchmarkReturn
                    ? -1
                    : 0;
            score += contribution;
            comparisons.Add(new DelphiLiveIntervalComparison(
                stockLeg.EndUtc,
                stockReturn,
                benchmarkReturn,
                contribution));
        }

        return new DelphiLivePersistenceMeasurements(
            DelphiLiveMeasurementAvailability.Available,
            comparisons.MoveToImmutable(),
            score,
            DelphiLiveReasonCodes.Available);
    }

    public static DelphiLivePriceMovementMeasurements CalculatePriceMovement(
        DelphiLiveFiveMinuteSeries stock,
        DelphiLiveFiveMinuteSeries benchmark,
        DateTime currentBarEndUtc,
        decimal? previousCanonicalSessionClose,
        DelphiLivePolicyDefinition policy,
        decimal? previousCanonicalXiuSessionClose = null)
    {
        ValidateInputs(stock, benchmark, currentBarEndUtc, policy);

        DelphiLiveWindowReturnMeasurement twentyMinute = CalculatePairedWindow(
            stock,
            benchmark,
            currentBarEndUtc,
            policy.ImmediateMovementHorizon,
            policy.BarInterval);
        DelphiLiveWindowReturnMeasurement oneHour = CalculatePairedWindow(
            stock,
            benchmark,
            currentBarEndUtc,
            policy.SustainedMovementHorizon,
            policy.BarInterval);
        DelphiLiveWindowReturnMeasurement twoHour = CalculatePairedWindow(
            stock,
            benchmark,
            currentBarEndUtc,
            policy.TwoHourContextHorizon,
            policy.BarInterval);
        DelphiLiveWindowReturnMeasurement threeHour = CalculatePairedWindow(
            stock,
            benchmark,
            currentBarEndUtc,
            policy.ThreeHourContextHorizon,
            policy.BarInterval);

        DelphiLiveScalarMeasurement currentClose = GetOperationalClose(stock, currentBarEndUtc);
        DelphiLiveScalarMeasurement previousCloseReturn;
        if (currentClose.Availability != DelphiLiveMeasurementAvailability.Available)
        {
            previousCloseReturn = DelphiLiveScalarMeasurement.Unavailable(currentClose.ReasonCode);
        }
        else if (previousCanonicalSessionClose is not > 0m)
        {
            previousCloseReturn = DelphiLiveScalarMeasurement.Unavailable(
                DelphiLiveReasonCodes.MissingExactEndpoint);
        }
        else
        {
            previousCloseReturn = DelphiLiveScalarMeasurement.Available(
                Return(previousCanonicalSessionClose.Value, currentClose.RequireValue()));
        }

        DelphiLiveScalarMeasurement xiuClose = GetOperationalClose(benchmark, currentBarEndUtc);
        DelphiLiveScalarMeasurement xiuPreviousCloseReturn =
            xiuClose.Availability == DelphiLiveMeasurementAvailability.Available &&
            previousCanonicalXiuSessionClose is > 0m
                ? DelphiLiveScalarMeasurement.Available(
                    Return(previousCanonicalXiuSessionClose.Value, xiuClose.RequireValue()))
                : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.MissingExactEndpoint);
        DelphiLiveScalarMeasurement previousCloseExcess =
            previousCloseReturn.Availability == DelphiLiveMeasurementAvailability.Available &&
            xiuPreviousCloseReturn.Availability == DelphiLiveMeasurementAvailability.Available
                ? DelphiLiveScalarMeasurement.Available(
                    previousCloseReturn.RequireValue() - xiuPreviousCloseReturn.RequireValue())
                : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.RelativeBaselineUnavailable);

        return new DelphiLivePriceMovementMeasurements(
            currentBarEndUtc,
            twentyMinute,
            oneHour,
            twoHour,
            threeHour,
            previousCloseReturn)
        {
            PreviousCloseBenchmarkReturn = xiuPreviousCloseReturn,
            PreviousCloseExcessReturn = previousCloseExcess
        };
    }

    public static DelphiLiveVolatilityRulerMeasurements CalculateVolatilityRulers(
        IReadOnlyList<DelphiLiveDailyBar> dailyBars,
        IReadOnlyList<DateOnly> canonicalSessionDates,
        DateOnly liveSessionDate,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(dailyBars);
        ArgumentNullException.ThrowIfNull(canonicalSessionDates);
        policy.Validate();

        DelphiLiveVolatilityRulerPolicy rulers = policy.VolatilityRulers;
        return new DelphiLiveVolatilityRulerMeasurements(
            CalculateTrueRangeRuler(
                dailyBars,
                canonicalSessionDates,
                liveSessionDate,
                rulers.DiagnosticShortSessions),
            CalculateTrueRangeRuler(
                dailyBars,
                canonicalSessionDates,
                liveSessionDate,
                rulers.OperationalSessions),
            CalculateTrueRangeRuler(
                dailyBars,
                canonicalSessionDates,
                liveSessionDate,
                rulers.ChallengerSessions),
            CalculateTrueRangeRuler(
                dailyBars,
                canonicalSessionDates,
                liveSessionDate,
                rulers.DiagnosticLongSessions));
    }

    public static DelphiLiveDirectionalVolumeMeasurements CalculateDirectionalVolume(
        DelphiLiveFiveMinuteSeries stock,
        DateTime currentBarEndUtc,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(stock);
        RequireUtc(currentBarEndUtc, nameof(currentBarEndUtc));
        policy.Validate();
        ValidateSeriesContract(stock, policy);

        RollingIntervals intervals = BuildRollingIntervals(
            stock,
            currentBarEndUtc,
            policy.DirectionalVolumeObservationCount,
            policy.BarInterval);
        if (intervals.Availability != DelphiLiveMeasurementAvailability.Available)
        {
            DelphiLiveScalarMeasurement unavailable = FromAvailability(
                intervals.Availability,
                intervals.ReasonCode);
            return new DelphiLiveDirectionalVolumeMeasurements(
                currentBarEndUtc,
                unavailable,
                unavailable,
                null);
        }

        decimal weightedVolume = 0m;
        long totalVolume = 0L;
        foreach (RollingIntervalLeg interval in intervals.Legs)
        {
            int direction = interval.EndPrice > interval.StartPrice
                ? 1
                : interval.EndPrice < interval.StartPrice
                    ? -1
                    : 0;
            weightedVolume += direction * (decimal)interval.Volume;
            totalVolume = checked(totalVolume + interval.Volume);
        }

        decimal priceReturn = Return(
            intervals.Legs[0].StartPrice,
            intervals.Legs[^1].EndPrice);
        DelphiLiveScalarMeasurement priceMeasurement =
            DelphiLiveScalarMeasurement.Available(priceReturn);
        if (totalVolume == 0L)
        {
            return new DelphiLiveDirectionalVolumeMeasurements(
                currentBarEndUtc,
                DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.ZeroTotalVolume),
                priceMeasurement,
                0L);
        }

        return new DelphiLiveDirectionalVolumeMeasurements(
            currentBarEndUtc,
            DelphiLiveScalarMeasurement.Available(weightedVolume / totalVolume),
            priceMeasurement,
            totalVolume);
    }

    public static DelphiLiveScalarMeasurement CalculateFullDayVolumeFraction(
        DelphiLiveFiveMinuteSeries stock,
        DateTime currentBarEndUtc,
        IReadOnlyList<DelphiLiveDailyBar> dailyBars,
        IReadOnlyList<DateOnly> canonicalSessionDates,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(dailyBars);
        ArgumentNullException.ThrowIfNull(canonicalSessionDates);
        RequireUtc(currentBarEndUtc, nameof(currentBarEndUtc));
        policy.Validate();
        ValidateSeriesContract(stock, policy);

        SessionBars sessionBars = GetSessionBarsThrough(
            stock,
            currentBarEndUtc,
            policy.BarInterval);
        if (sessionBars.Availability != DelphiLiveMeasurementAvailability.Available)
            return FromAvailability(sessionBars.Availability, sessionBars.ReasonCode);

        DelphiLiveScalarMeasurement medianVolume = CalculateDailyVolumeMedian(
            dailyBars,
            canonicalSessionDates,
            stock.SessionDate,
            policy.FullDayVolumeMedianSessionCount);
        if (medianVolume.Availability != DelphiLiveMeasurementAvailability.Available)
            return DelphiLiveScalarMeasurement.Unavailable(medianVolume.ReasonCode);

        decimal cumulativeVolume = 0m;
        foreach (DelphiLiveFiveMinuteBar bar in sessionBars.Bars)
            cumulativeVolume += bar.Volume;
        decimal baseline = medianVolume.RequireValue();
        if (baseline <= 0m)
            return DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.ZeroTotalVolume);
        return DelphiLiveScalarMeasurement.Available(cumulativeVolume / baseline);
    }

    public static DelphiLiveScalarMeasurement CalculateSessionVwap(
        DelphiLiveFiveMinuteSeries stock,
        DateTime currentBarEndUtc,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(stock);
        RequireUtc(currentBarEndUtc, nameof(currentBarEndUtc));
        policy.Validate();
        ValidateSeriesContract(stock, policy);

        SessionBars path = GetSessionBarsThrough(stock, currentBarEndUtc, policy.BarInterval);
        if (path.Availability != DelphiLiveMeasurementAvailability.Available)
            return FromAvailability(path.Availability, path.ReasonCode);

        decimal weightedPrice = 0m;
        decimal totalVolume = 0m;
        foreach (DelphiLiveFiveMinuteBar bar in path.Bars)
        {
            decimal typicalPrice = (bar.High + bar.Low + bar.Close) / 3m;
            weightedPrice += typicalPrice * bar.Volume;
            totalVolume += bar.Volume;
        }

        if (totalVolume <= 0m)
            return DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.ZeroTotalVolume);
        return DelphiLiveScalarMeasurement.Available(weightedPrice / totalVolume);
    }

    public static DelphiLivePriorRangeMeasurements CalculatePriorTwentyMinuteRange(
        DelphiLiveFiveMinuteSeries stock,
        DateTime currentBarEndUtc,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(stock);
        RequireUtc(currentBarEndUtc, nameof(currentBarEndUtc));
        policy.Validate();
        ValidateSeriesContract(stock, policy);

        DelphiLiveScalarMeasurement currentClose = GetOperationalClose(stock, currentBarEndUtc);
        if (currentClose.Availability != DelphiLiveMeasurementAvailability.Available)
        {
            return new DelphiLivePriorRangeMeasurements(
                currentClose.Availability,
                null,
                null,
                currentClose.ReasonCode);
        }

        DateTime earliestPriorEndUtc = currentBarEndUtc -
            Multiply(policy.BarInterval, policy.PriorRangeObservationCount);
        DateTime earliestPermittedEndUtc = stock.OperationalContinuityStartUtc == stock.SessionOpenUtc
            ? stock.SessionOpenUtc + policy.BarInterval
            : stock.OperationalContinuityStartUtc;
        if (earliestPriorEndUtc < earliestPermittedEndUtc)
        {
            return new DelphiLivePriorRangeMeasurements(
                DelphiLiveMeasurementAvailability.NotMature,
                null,
                null,
                DelphiLiveReasonCodes.NotMature);
        }

        decimal? high = null;
        decimal? low = null;
        for (int index = policy.PriorRangeObservationCount; index >= 1; index--)
        {
            DateTime expectedEndUtc = currentBarEndUtc - Multiply(policy.BarInterval, index);
            BarLookup lookup = FindOperationalBar(stock, expectedEndUtc);
            if (lookup.Bar is null)
            {
                return new DelphiLivePriorRangeMeasurements(
                    DelphiLiveMeasurementAvailability.Unavailable,
                    null,
                    null,
                    lookup.ReasonCode);
            }

            high = high.HasValue ? System.Math.Max(high.Value, lookup.Bar.High) : lookup.Bar.High;
            low = low.HasValue ? System.Math.Min(low.Value, lookup.Bar.Low) : lookup.Bar.Low;
        }

        return new DelphiLivePriorRangeMeasurements(
            DelphiLiveMeasurementAvailability.Available,
            high,
            low,
            DelphiLiveReasonCodes.Available);
    }

    public static DelphiLivePriceStructureMeasurements CalculatePriceStructure(
        DelphiLiveFiveMinuteSeries stock,
        DateTime currentBarEndUtc,
        decimal? previousCanonicalSessionClose,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(stock);
        RequireUtc(currentBarEndUtc, nameof(currentBarEndUtc));
        policy.Validate();
        ValidateSeriesContract(stock, policy);

        DelphiLiveScalarMeasurement currentClose = GetOperationalClose(stock, currentBarEndUtc);
        DelphiLiveScalarMeasurement previousClose = previousCanonicalSessionClose is > 0m
            ? DelphiLiveScalarMeasurement.Available(previousCanonicalSessionClose.Value)
            : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.MissingExactEndpoint);

        return new DelphiLivePriceStructureMeasurements(
            currentBarEndUtc,
            currentClose,
            previousClose,
            CalculateSessionVwap(stock, currentBarEndUtc, policy),
            CalculatePriorTwentyMinuteRange(stock, currentBarEndUtc, policy));
    }

    private static DelphiLiveWindowReturnMeasurement CalculatePairedWindow(
        DelphiLiveFiveMinuteSeries stock,
        DelphiLiveFiveMinuteSeries benchmark,
        DateTime currentBarEndUtc,
        TimeSpan horizon,
        TimeSpan interval)
    {
        DelphiLiveScalarMeasurement stockReturn = CalculateRollingReturn(
            stock,
            currentBarEndUtc,
            horizon,
            interval);
        DelphiLiveScalarMeasurement benchmarkReturn = CalculateRollingReturn(
            benchmark,
            currentBarEndUtc,
            horizon,
            interval);

        DelphiLiveScalarMeasurement excessReturn;
        DelphiLiveMeasurementAvailability combined = CombineAvailability(
            stockReturn.Availability,
            benchmarkReturn.Availability);
        if (combined == DelphiLiveMeasurementAvailability.Available)
        {
            excessReturn = DelphiLiveScalarMeasurement.Available(
                stockReturn.RequireValue() - benchmarkReturn.RequireValue());
        }
        else
        {
            string reason = stockReturn.Availability != DelphiLiveMeasurementAvailability.Available
                ? stockReturn.ReasonCode
                : benchmarkReturn.ReasonCode;
            excessReturn = FromAvailability(combined, reason);
        }

        return new DelphiLiveWindowReturnMeasurement(
            horizon,
            stockReturn,
            benchmarkReturn,
            excessReturn);
    }

    private static DelphiLiveScalarMeasurement CalculateRollingReturn(
        DelphiLiveFiveMinuteSeries series,
        DateTime currentBarEndUtc,
        TimeSpan horizon,
        TimeSpan interval)
    {
        if (horizon <= TimeSpan.Zero || horizon.Ticks % interval.Ticks != 0)
            throw new ArgumentException("A rolling horizon must contain a whole positive number of bars.", nameof(horizon));
        int count = checked((int)(horizon.Ticks / interval.Ticks));
        RollingIntervals intervals = BuildRollingIntervals(series, currentBarEndUtc, count, interval);
        if (intervals.Availability != DelphiLiveMeasurementAvailability.Available)
            return FromAvailability(intervals.Availability, intervals.ReasonCode);
        return DelphiLiveScalarMeasurement.Available(
            Return(intervals.Legs[0].StartPrice, intervals.Legs[^1].EndPrice));
    }

    private static RollingIntervals BuildRollingIntervals(
        DelphiLiveFiveMinuteSeries series,
        DateTime currentBarEndUtc,
        int observationCount,
        TimeSpan interval)
    {
        TimeSpan horizon = Multiply(interval, observationCount);
        DateTime startEndpointUtc = currentBarEndUtc - horizon;
        if (startEndpointUtc < series.OperationalContinuityStartUtc ||
            currentBarEndUtc < series.OperationalContinuityStartUtc + horizon)
        {
            return RollingIntervals.NotMature();
        }
        if ((startEndpointUtc - series.SessionOpenUtc).Ticks % interval.Ticks != 0)
            return RollingIntervals.Unavailable(DelphiLiveReasonCodes.MissingExactEndpoint);

        decimal startPrice;
        bool startsAtSessionOpen = startEndpointUtc == series.SessionOpenUtc &&
            series.OperationalContinuityStartUtc == series.SessionOpenUtc;
        if (startsAtSessionOpen)
        {
            BarLookup openingLookup = FindOperationalBar(series, series.SessionOpenUtc + interval);
            if (openingLookup.Bar is null || openingLookup.Bar.StartUtc != series.SessionOpenUtc)
                return RollingIntervals.Unavailable(openingLookup.ReasonCode);
            startPrice = openingLookup.Bar.Open;
        }
        else
        {
            BarLookup startingLookup = FindOperationalBar(series, startEndpointUtc);
            if (startingLookup.Bar is null)
                return RollingIntervals.Unavailable(startingLookup.ReasonCode);
            startPrice = startingLookup.Bar.Close;
        }

        var legs = ImmutableArray.CreateBuilder<RollingIntervalLeg>(observationCount);
        decimal priorPrice = startPrice;
        for (int index = 1; index <= observationCount; index++)
        {
            DateTime expectedEndUtc = startEndpointUtc + Multiply(interval, index);
            BarLookup lookup = FindOperationalBar(series, expectedEndUtc);
            if (lookup.Bar is null || lookup.Bar.StartUtc != expectedEndUtc - interval)
                return RollingIntervals.Unavailable(lookup.ReasonCode);

            legs.Add(new RollingIntervalLeg(
                expectedEndUtc,
                priorPrice,
                lookup.Bar.Close,
                lookup.Bar.Volume));
            priorPrice = lookup.Bar.Close;
        }

        return RollingIntervals.Available(legs.MoveToImmutable());
    }

    private static SessionBars GetSessionBarsThrough(
        DelphiLiveFiveMinuteSeries series,
        DateTime currentBarEndUtc,
        TimeSpan interval)
    {
        if (currentBarEndUtc < series.SessionOpenUtc + interval)
            return SessionBars.NotMature();
        long elapsedTicks = (currentBarEndUtc - series.SessionOpenUtc).Ticks;
        if (elapsedTicks % interval.Ticks != 0)
            return SessionBars.Unavailable(DelphiLiveReasonCodes.MissingExactEndpoint);

        int expectedCount = checked((int)(elapsedTicks / interval.Ticks));
        var bars = ImmutableArray.CreateBuilder<DelphiLiveFiveMinuteBar>(expectedCount);
        for (int index = 1; index <= expectedCount; index++)
        {
            DateTime expectedEndUtc = series.SessionOpenUtc + Multiply(interval, index);
            BarLookup lookup = FindOperationalBar(series, expectedEndUtc);
            if (lookup.Bar is null || lookup.Bar.StartUtc != expectedEndUtc - interval)
                return SessionBars.Unavailable(lookup.ReasonCode);
            bars.Add(lookup.Bar);
        }
        return SessionBars.Available(bars.MoveToImmutable());
    }

    private static DelphiLiveTrueRangeRulerMeasurement CalculateTrueRangeRuler(
        IReadOnlyList<DelphiLiveDailyBar> dailyBars,
        IReadOnlyList<DateOnly> canonicalSessionDates,
        DateOnly liveSessionDate,
        int sessionCount)
    {
        if (!TryGetAlignedDailyBars(
                dailyBars,
                canonicalSessionDates,
                liveSessionDate,
                sessionCount + 1,
                out ImmutableArray<DelphiLiveDailyBar> aligned,
                out DateOnly? sourceThrough,
                out string reasonCode))
        {
            return new DelphiLiveTrueRangeRulerMeasurement(
                sessionCount,
                sourceThrough,
                DelphiLiveScalarMeasurement.Unavailable(reasonCode));
        }

        var trueRangePercentages = new decimal[sessionCount];
        for (int index = 1; index < aligned.Length; index++)
        {
            DelphiLiveDailyBar current = aligned[index];
            decimal previousClose = aligned[index - 1].Close;
            decimal trueRange = System.Math.Max(
                current.High - current.Low,
                System.Math.Max(
                    System.Math.Abs(current.High - previousClose),
                    System.Math.Abs(current.Low - previousClose)));
            trueRangePercentages[index - 1] = trueRange / previousClose;
        }

        return new DelphiLiveTrueRangeRulerMeasurement(
            sessionCount,
            sourceThrough,
            DelphiLiveScalarMeasurement.Available(Median(trueRangePercentages)));
    }

    private static DelphiLiveScalarMeasurement CalculateDailyVolumeMedian(
        IReadOnlyList<DelphiLiveDailyBar> dailyBars,
        IReadOnlyList<DateOnly> canonicalSessionDates,
        DateOnly liveSessionDate,
        int sessionCount)
    {
        if (!TryGetAlignedDailyBars(
                dailyBars,
                canonicalSessionDates,
                liveSessionDate,
                sessionCount,
                out ImmutableArray<DelphiLiveDailyBar> aligned,
                out _,
                out string reasonCode))
            return DelphiLiveScalarMeasurement.Unavailable(reasonCode);

        decimal[] volumes = aligned.Select(bar => (decimal)bar.Volume).ToArray();
        decimal median = Median(volumes);
        return median > 0m
            ? DelphiLiveScalarMeasurement.Available(median)
            : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.ZeroTotalVolume);
    }

    private static bool TryGetAlignedDailyBars(
        IReadOnlyList<DelphiLiveDailyBar> dailyBars,
        IReadOnlyList<DateOnly> canonicalSessionDates,
        DateOnly liveSessionDate,
        int requiredCount,
        out ImmutableArray<DelphiLiveDailyBar> aligned,
        out DateOnly? sourceThrough,
        out string reasonCode)
    {
        aligned = ImmutableArray<DelphiLiveDailyBar>.Empty;
        sourceThrough = null;
        reasonCode = DelphiLiveReasonCodes.BaselineUnavailable;
        if (requiredCount <= 0 || canonicalSessionDates.Count < requiredCount)
            return false;

        DateOnly prior = default;
        var seenCanonicalDates = new HashSet<DateOnly>();
        for (int index = 0; index < canonicalSessionDates.Count; index++)
        {
            DateOnly date = canonicalSessionDates[index];
            if (date >= liveSessionDate || !seenCanonicalDates.Add(date) || (index > 0 && date <= prior))
                return false;
            prior = date;
        }

        var byDate = new Dictionary<DateOnly, DelphiLiveDailyBar>();
        string? symbol = null;
        foreach (DelphiLiveDailyBar bar in dailyBars)
        {
            if (bar is null)
                return false;
            symbol ??= bar.Symbol;
            if (!string.Equals(symbol, bar.Symbol, StringComparison.Ordinal) || !byDate.TryAdd(bar.SessionDate, bar))
                return false;
        }

        var builder = ImmutableArray.CreateBuilder<DelphiLiveDailyBar>(requiredCount);
        int firstIndex = canonicalSessionDates.Count - requiredCount;
        for (int index = firstIndex; index < canonicalSessionDates.Count; index++)
        {
            DateOnly date = canonicalSessionDates[index];
            if (!byDate.TryGetValue(date, out DelphiLiveDailyBar? bar))
                return false;
            builder.Add(bar);
        }

        aligned = builder.MoveToImmutable();
        sourceThrough = canonicalSessionDates[^1];
        reasonCode = DelphiLiveReasonCodes.Available;
        return true;
    }

    private static DelphiLiveScalarMeasurement GetOperationalClose(
        DelphiLiveFiveMinuteSeries series,
        DateTime endUtc)
    {
        BarLookup lookup = FindOperationalBar(series, endUtc);
        return lookup.Bar is null
            ? DelphiLiveScalarMeasurement.Unavailable(lookup.ReasonCode)
            : DelphiLiveScalarMeasurement.Available(lookup.Bar.Close);
    }

    private static BarLookup FindOperationalBar(
        DelphiLiveFiveMinuteSeries series,
        DateTime endUtc)
    {
        foreach (DelphiLiveFiveMinuteBar bar in series.Bars)
        {
            if (bar.EndUtc != endUtc)
                continue;
            return bar.Disposition == DelphiLiveEvidenceDisposition.OperationalOnTime
                ? new BarLookup(bar, DelphiLiveReasonCodes.Available)
                : new BarLookup(null, DelphiLiveReasonCodes.LateResearchOnly);
        }
        return new BarLookup(null, DelphiLiveReasonCodes.MissingExactEndpoint);
    }

    private static DelphiLiveMeasurementAvailability CombineAvailability(
        DelphiLiveMeasurementAvailability first,
        DelphiLiveMeasurementAvailability second)
    {
        if (first == DelphiLiveMeasurementAvailability.Unavailable ||
            second == DelphiLiveMeasurementAvailability.Unavailable)
            return DelphiLiveMeasurementAvailability.Unavailable;
        if (first == DelphiLiveMeasurementAvailability.NotMature ||
            second == DelphiLiveMeasurementAvailability.NotMature)
            return DelphiLiveMeasurementAvailability.NotMature;
        return DelphiLiveMeasurementAvailability.Available;
    }

    private static string FirstUnavailableReason(
        RollingIntervals first,
        RollingIntervals second) =>
        first.Availability != DelphiLiveMeasurementAvailability.Available
            ? first.ReasonCode
            : second.ReasonCode;

    private static DelphiLiveScalarMeasurement FromAvailability(
        DelphiLiveMeasurementAvailability availability,
        string reasonCode) =>
        availability == DelphiLiveMeasurementAvailability.NotMature
            ? DelphiLiveScalarMeasurement.NotMature(reasonCode)
            : DelphiLiveScalarMeasurement.Unavailable(reasonCode);

    private static decimal Return(decimal startPrice, decimal endPrice) =>
        endPrice / startPrice - 1m;

    private static decimal Median(decimal[] values)
    {
        if (values.Length == 0)
            throw new ArgumentException("A median requires at least one value.", nameof(values));
        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2m;
    }

    private static TimeSpan Multiply(TimeSpan value, int multiplier) =>
        TimeSpan.FromTicks(checked(value.Ticks * multiplier));

    private static void ValidateInputs(
        DelphiLiveFiveMinuteSeries stock,
        DelphiLiveFiveMinuteSeries benchmark,
        DateTime currentBarEndUtc,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(benchmark);
        RequireUtc(currentBarEndUtc, nameof(currentBarEndUtc));
        policy.Validate();
        if (stock.SessionDate != benchmark.SessionDate || stock.SessionOpenUtc != benchmark.SessionOpenUtc)
            throw new ArgumentException("Stock and benchmark evidence must use the same session and timestamps.", nameof(benchmark));
        ValidateSeriesContract(stock, policy);
        ValidateSeriesContract(benchmark, policy);
    }

    private static void ValidateSeriesContract(
        DelphiLiveFiveMinuteSeries series,
        DelphiLivePolicyDefinition policy)
    {
        foreach (DelphiLiveFiveMinuteBar bar in series.Bars)
        {
            if (bar.SourceContractVersion != policy.CollectorSourceContractVersion)
            {
                throw new ArgumentException(
                    "Five-minute evidence uses an unsupported source-contract version.",
                    nameof(series));
            }
        }
    }

    private static void RequireUtc(DateTime timestamp, string parameterName)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }

    private sealed record RollingIntervalLeg(
        DateTime EndUtc,
        decimal StartPrice,
        decimal EndPrice,
        long Volume);

    private sealed record RollingIntervals(
        DelphiLiveMeasurementAvailability Availability,
        ImmutableArray<RollingIntervalLeg> Legs,
        string ReasonCode)
    {
        public static RollingIntervals Available(ImmutableArray<RollingIntervalLeg> legs) =>
            new(DelphiLiveMeasurementAvailability.Available, legs, DelphiLiveReasonCodes.Available);

        public static RollingIntervals NotMature() =>
            new(
                DelphiLiveMeasurementAvailability.NotMature,
                ImmutableArray<RollingIntervalLeg>.Empty,
                DelphiLiveReasonCodes.NotMature);

        public static RollingIntervals Unavailable(string reasonCode) =>
            new(
                DelphiLiveMeasurementAvailability.Unavailable,
                ImmutableArray<RollingIntervalLeg>.Empty,
                reasonCode);
    }

    private sealed record SessionBars(
        DelphiLiveMeasurementAvailability Availability,
        ImmutableArray<DelphiLiveFiveMinuteBar> Bars,
        string ReasonCode)
    {
        public static SessionBars Available(ImmutableArray<DelphiLiveFiveMinuteBar> bars) =>
            new(DelphiLiveMeasurementAvailability.Available, bars, DelphiLiveReasonCodes.Available);

        public static SessionBars NotMature() =>
            new(
                DelphiLiveMeasurementAvailability.NotMature,
                ImmutableArray<DelphiLiveFiveMinuteBar>.Empty,
                DelphiLiveReasonCodes.NotMature);

        public static SessionBars Unavailable(string reasonCode) =>
            new(
                DelphiLiveMeasurementAvailability.Unavailable,
                ImmutableArray<DelphiLiveFiveMinuteBar>.Empty,
                reasonCode);
    }

    private sealed record BarLookup(DelphiLiveFiveMinuteBar? Bar, string ReasonCode);
}
