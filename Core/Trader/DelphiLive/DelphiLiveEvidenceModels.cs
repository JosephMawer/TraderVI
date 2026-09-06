#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveEvidenceDisposition
{
    OperationalOnTime,
    LateResearchOnly
}

public enum DelphiLiveMeasurementAvailability
{
    Unavailable,
    NotMature,
    Available
}

public static class DelphiLiveReasonCodes
{
    public const string Available = "Available";
    public const string NotMature = "NotMature";
    public const string Unavailable = "Unavailable";
    public const string MissingExactEndpoint = "MissingExactEndpoint";
    public const string MissingContiguousPath = "MissingContiguousPath";
    public const string LateResearchOnly = "LateResearchOnly";
    public const string ZeroTotalVolume = "ZeroTotalVolume";
    public const string BaselineUnavailable = "BaselineUnavailable";
    public const string RelativeBaselineUnavailable = "RelativeBaselineUnavailable";
    public const string RawMoveBelowThreshold = "RawMoveBelowThreshold";
    public const string RawMoveWithoutRelativeAgreement = "RawMoveWithoutRelativeAgreement";
    public const string RelativeMoveWithoutRawAgreement = "RelativeMoveWithoutRawAgreement";
    public const string RawRelativeConflict = "RawRelativeConflict";
    public const string RisingButLagging = "RisingButLagging";
    public const string FallingButOutperforming = "FallingButOutperforming";
    public const string MixedOrFlat = "MixedOrFlat";
    public const string DirectionalVolumeWithinDeadband = "DirectionalVolumeWithinDeadband";
    public const string VolumePriceConflict = "VolumePriceConflict";
    public const string VolumePriceNotDirectional = "VolumePriceNotDirectional";
    public const string TwentyMinuteOnly = "20mOnly";
    public const string WindowsAgree = "WindowsAgree";
    public const string TwentyMinuteCarries = "TwentyMinuteCarries";
    public const string OneHourCarries = "OneHourCarries";
    public const string MeaningfulWindowConflict = "MeaningfulWindowConflict";
    public const string NoMeaningfulVotingWindow = "NoMeaningfulVotingWindow";
    public const string PersistenceSupportive = "PersistenceSupportive";
    public const string PersistencePositiveLeaning = "PersistencePositiveLeaning";
    public const string PersistenceNeutral = "PersistenceNeutral";
    public const string PersistenceNegativeLeaning = "PersistenceNegativeLeaning";
    public const string PersistenceWeakening = "PersistenceWeakening";
    public const string DirectionalVolumeSupportive = "DirectionalVolumeSupportive";
    public const string DirectionalVolumeWeakening = "DirectionalVolumeWeakening";
    public const string PriceStructureSupportive = "PriceStructureSupportive";
    public const string PriceStructurePositiveLeaning = "PriceStructurePositiveLeaning";
    public const string PriceStructureNeutral = "PriceStructureNeutral";
    public const string PriceStructureNeutralConflict = "PriceStructureNeutralConflict";
    public const string PriceStructureNegativeLeaning = "PriceStructureNegativeLeaning";
    public const string PriceStructureWeakening = "PriceStructureWeakening";
}

public readonly record struct DelphiLiveScalarMeasurement(
    DelphiLiveMeasurementAvailability Availability,
    decimal? Value,
    string ReasonCode)
{
    public static DelphiLiveScalarMeasurement Available(
        decimal value,
        string reasonCode = DelphiLiveReasonCodes.Available) =>
        new(DelphiLiveMeasurementAvailability.Available, value, RequireReason(reasonCode));

    public static DelphiLiveScalarMeasurement NotMature(
        string reasonCode = DelphiLiveReasonCodes.NotMature) =>
        new(DelphiLiveMeasurementAvailability.NotMature, null, RequireReason(reasonCode));

    public static DelphiLiveScalarMeasurement Unavailable(
        string reasonCode = DelphiLiveReasonCodes.Unavailable) =>
        new(DelphiLiveMeasurementAvailability.Unavailable, null, RequireReason(reasonCode));

    public decimal RequireValue()
    {
        if (Availability != DelphiLiveMeasurementAvailability.Available || !Value.HasValue)
            throw new InvalidOperationException("The measurement has no available value.");
        return Value.Value;
    }

    private static string RequireReason(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("A stable reason code is required.", nameof(reasonCode));
        return reasonCode;
    }
}

public sealed record DelphiLiveFiveMinuteBar
{
    public DelphiLiveFiveMinuteBar(
        Guid observationId,
        string symbol,
        DateOnly sessionDate,
        DateTime startUtc,
        DateTime endUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        DateTime receivedUtc,
        string provider,
        int sourceContractVersion,
        DelphiLiveEvidenceDisposition disposition)
    {
        if (observationId == Guid.Empty)
            throw new ArgumentException("Observation identity is required.", nameof(observationId));
        Symbol = RequireCanonicalSymbol(symbol);
        RequireUtc(startUtc, nameof(startUtc));
        RequireUtc(endUtc, nameof(endUtc));
        RequireUtc(receivedUtc, nameof(receivedUtc));
        if (endUtc - startUtc != TimeSpan.FromMinutes(5))
            throw new ArgumentException("A canonical Delphi Live bar is exactly five minutes.", nameof(endUtc));
        if (startUtc.Second != 0 || startUtc.Millisecond != 0 || startUtc.Ticks % TimeSpan.TicksPerSecond != 0 ||
            endUtc.Second != 0 || endUtc.Millisecond != 0 || endUtc.Ticks % TimeSpan.TicksPerSecond != 0 ||
            startUtc.Minute % 5 != 0 || endUtc.Minute % 5 != 0)
            throw new ArgumentException("Canonical five-minute timestamps must be exact five-minute boundaries.", nameof(startUtc));
        RequireOhlc(open, high, low, close);
        if (volume < 0)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume cannot be negative.");
        if (receivedUtc <= endUtc)
            throw new ArgumentException("A completed bar must be received after its interval ends.", nameof(receivedUtc));
        if (string.IsNullOrWhiteSpace(provider) || provider != provider.Trim())
            throw new ArgumentException("A canonical provider identity is required.", nameof(provider));
        if (sourceContractVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceContractVersion));
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));

        ObservationId = observationId;
        SessionDate = sessionDate;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        ReceivedUtc = receivedUtc;
        Provider = provider;
        SourceContractVersion = sourceContractVersion;
        Disposition = disposition;
    }

    public Guid ObservationId { get; }
    public string Symbol { get; }
    public DateOnly SessionDate { get; }
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }
    public decimal Open { get; }
    public decimal High { get; }
    public decimal Low { get; }
    public decimal Close { get; }
    public long Volume { get; }
    public DateTime ReceivedUtc { get; }
    public string Provider { get; }
    public int SourceContractVersion { get; }
    public DelphiLiveEvidenceDisposition Disposition { get; }

    private static void RequireOhlc(decimal open, decimal high, decimal low, decimal close)
    {
        if (open <= 0m || high <= 0m || low <= 0m || close <= 0m)
            throw new ArgumentOutOfRangeException(nameof(open), "OHLC values must be positive.");
        if (low > System.Math.Min(open, close))
            throw new ArgumentException("Low must not exceed open or close.", nameof(low));
        if (high < System.Math.Max(open, close))
            throw new ArgumentException("High must not be below open or close.", nameof(high));
        if (low > high)
            throw new ArgumentException("Low must not exceed high.", nameof(low));
    }

    internal static string RequireCanonicalSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) ||
            symbol != symbol.Trim() ||
            !string.Equals(symbol, symbol.ToUpperInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("Symbol must be a non-empty canonical uppercase ticker.", nameof(symbol));
        return symbol;
    }

    internal static void RequireUtc(DateTime timestamp, string parameterName)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}

public sealed record DelphiLiveDailyBar
{
    public DelphiLiveDailyBar(
        Guid observationId,
        string symbol,
        DateOnly sessionDate,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume)
    {
        if (observationId == Guid.Empty)
            throw new ArgumentException("Observation identity is required.", nameof(observationId));
        Symbol = DelphiLiveFiveMinuteBar.RequireCanonicalSymbol(symbol);
        if (open <= 0m || high <= 0m || low <= 0m || close <= 0m)
            throw new ArgumentOutOfRangeException(nameof(open), "OHLC values must be positive.");
        if (low > System.Math.Min(open, close) || high < System.Math.Max(open, close) || low > high)
            throw new ArgumentException("Daily OHLC structure is invalid.", nameof(low));
        if (volume < 0)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume cannot be negative.");

        ObservationId = observationId;
        SessionDate = sessionDate;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }

    public Guid ObservationId { get; }
    public string Symbol { get; }
    public DateOnly SessionDate { get; }
    public decimal Open { get; }
    public decimal High { get; }
    public decimal Low { get; }
    public decimal Close { get; }
    public long Volume { get; }
}

/// <summary>
/// An immutable view over a symbol's current-session bars. Gaps are retained so
/// measurement code can fail closed rather than compressing the path.
/// </summary>
public sealed record DelphiLiveFiveMinuteSeries
{
    [System.Text.Json.Serialization.JsonConstructor]
    public DelphiLiveFiveMinuteSeries(
        string symbol,
        DateOnly sessionDate,
        DateTime sessionOpenUtc,
        DateTime operationalContinuityStartUtc,
        ImmutableArray<DelphiLiveFiveMinuteBar> bars)
        : this(symbol, sessionDate, sessionOpenUtc, operationalContinuityStartUtc,
            (IEnumerable<DelphiLiveFiveMinuteBar>)bars)
    {
    }

    public DelphiLiveFiveMinuteSeries(
        string symbol,
        DateOnly sessionDate,
        DateTime sessionOpenUtc,
        DateTime operationalContinuityStartUtc,
        IEnumerable<DelphiLiveFiveMinuteBar> bars)
    {
        Symbol = DelphiLiveFiveMinuteBar.RequireCanonicalSymbol(symbol);
        DelphiLiveFiveMinuteBar.RequireUtc(sessionOpenUtc, nameof(sessionOpenUtc));
        DelphiLiveFiveMinuteBar.RequireUtc(operationalContinuityStartUtc, nameof(operationalContinuityStartUtc));
        if (sessionOpenUtc.Second != 0 || sessionOpenUtc.Millisecond != 0 || sessionOpenUtc.Minute % 5 != 0)
            throw new ArgumentException("Session open must be an exact five-minute boundary.", nameof(sessionOpenUtc));
        if (operationalContinuityStartUtc < sessionOpenUtc)
            throw new ArgumentException("Operational continuity cannot begin before the session.", nameof(operationalContinuityStartUtc));
        ArgumentNullException.ThrowIfNull(bars);

        ImmutableArray<DelphiLiveFiveMinuteBar> immutableBars = bars.ToImmutableArray();
        DateTime? priorEndUtc = null;
        var identities = new HashSet<Guid>();
        foreach (DelphiLiveFiveMinuteBar bar in immutableBars)
        {
            ArgumentNullException.ThrowIfNull(bar);
            if (!string.Equals(bar.Symbol, Symbol, StringComparison.Ordinal) || bar.SessionDate != sessionDate)
                throw new ArgumentException("Every bar must belong to the series symbol and session.", nameof(bars));
            if (bar.StartUtc < sessionOpenUtc)
                throw new ArgumentException("A regular-session series cannot contain a pre-open bar.", nameof(bars));
            if (priorEndUtc.HasValue && bar.StartUtc < priorEndUtc.Value)
                throw new ArgumentException("Bars must be ordered and cannot overlap.", nameof(bars));
            if (!identities.Add(bar.ObservationId))
                throw new ArgumentException("A canonical observation identity cannot repeat.", nameof(bars));
            priorEndUtc = bar.EndUtc;
        }

        SessionDate = sessionDate;
        SessionOpenUtc = sessionOpenUtc;
        OperationalContinuityStartUtc = operationalContinuityStartUtc;
        Bars = immutableBars;
    }

    public string Symbol { get; }
    public DateOnly SessionDate { get; }
    public DateTime SessionOpenUtc { get; }
    public DateTime OperationalContinuityStartUtc { get; }
    public ImmutableArray<DelphiLiveFiveMinuteBar> Bars { get; }
}

public sealed record DelphiLiveIntervalComparison(
    DateTime IntervalEndUtc,
    decimal StockReturn,
    decimal BenchmarkReturn,
    int Contribution);

public sealed record DelphiLivePersistenceMeasurements(
    DelphiLiveMeasurementAvailability Availability,
    ImmutableArray<DelphiLiveIntervalComparison> Intervals,
    int? Score,
    string ReasonCode);

public sealed record DelphiLiveWindowReturnMeasurement(
    TimeSpan Horizon,
    DelphiLiveScalarMeasurement StockReturn,
    DelphiLiveScalarMeasurement BenchmarkReturn,
    DelphiLiveScalarMeasurement ExcessReturn);

public sealed record DelphiLivePriceMovementMeasurements(
    DateTime BarEndUtc,
    DelphiLiveWindowReturnMeasurement TwentyMinute,
    DelphiLiveWindowReturnMeasurement OneHour,
    DelphiLiveWindowReturnMeasurement TwoHour,
    DelphiLiveWindowReturnMeasurement ThreeHour,
    DelphiLiveScalarMeasurement PreviousCloseReturn)
{
    public DelphiLiveScalarMeasurement PreviousCloseBenchmarkReturn { get; init; } =
        DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.MissingExactEndpoint);
    public DelphiLiveScalarMeasurement PreviousCloseExcessReturn { get; init; } =
        DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.RelativeBaselineUnavailable);
}

public sealed record DelphiLiveTrueRangeRulerMeasurement(
    int SessionCount,
    DateOnly? SourceThroughSession,
    DelphiLiveScalarMeasurement MedianTrueRangePct);

public sealed record DelphiLiveVolatilityRulerMeasurements(
    DelphiLiveTrueRangeRulerMeasurement FiveSession,
    DelphiLiveTrueRangeRulerMeasurement TenSession,
    DelphiLiveTrueRangeRulerMeasurement FourteenSession,
    DelphiLiveTrueRangeRulerMeasurement TwentySession)
{
    public DelphiLiveTrueRangeRulerMeasurement Operational => TenSession;

    public DelphiLiveTrueRangeRulerMeasurement Select(DelphiLivePolicyDefinition policy)
    {
        policy.Validate();
        return policy.SelectedRulerSessions switch
        {
            10 => TenSession,
            14 => FourteenSession,
            _ => throw new DelphiLivePolicyValidationException("Unsupported assigned volatility ruler.")
        };
    }
}

public sealed record DelphiLiveDirectionalVolumeMeasurements(
    DateTime BarEndUtc,
    DelphiLiveScalarMeasurement Balance,
    DelphiLiveScalarMeasurement TwentyMinutePriceReturn,
    long? TotalVolume);

public sealed record DelphiLivePriorRangeMeasurements(
    DelphiLiveMeasurementAvailability Availability,
    decimal? High,
    decimal? Low,
    string ReasonCode);

public sealed record DelphiLivePriceStructureMeasurements(
    DateTime BarEndUtc,
    DelphiLiveScalarMeasurement CurrentClose,
    DelphiLiveScalarMeasurement PreviousClose,
    DelphiLiveScalarMeasurement SessionVwap,
    DelphiLivePriorRangeMeasurements PriorTwentyMinuteRange);

public enum DelphiLiveSignalFamily
{
    Persistence,
    PriceMovement,
    VolumeSupport,
    PriceStructure
}

public enum DelphiLiveFamilyState
{
    NotMature,
    Unavailable,
    Supportive,
    PositiveLeaning,
    Neutral,
    NeutralConflict,
    NegativeLeaning,
    Weakening
}

public sealed record DelphiLiveFamilyJudgment
{
    public DelphiLiveFamilyJudgment(
        DelphiLiveSignalFamily family,
        DelphiLiveFamilyState state,
        string reasonCode)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("A stable family reason code is required.", nameof(reasonCode));

        Family = family;
        State = state;
        ReasonCode = reasonCode;
    }

    public DelphiLiveSignalFamily Family { get; }
    public DelphiLiveFamilyState State { get; }
    public string ReasonCode { get; }
}
