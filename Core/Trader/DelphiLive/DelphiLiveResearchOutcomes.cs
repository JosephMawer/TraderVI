#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public enum DelphiLiveOutcomeMetricState
{
    Valid,
    Degraded,
    Invalid,
    Pending,
    NotApplicable
}

public enum DelphiLiveOutcomeHorizon
{
    Minutes20,
    Minutes60,
    Minutes120,
    Minutes180,
    Session1,
    Session3,
    Session5
}

public enum DelphiLiveOutcomeEvidenceBasket
{
    ModelGrade,
    NearEligible,
    OutOfScopeValid,
    Unusable
}

public enum DelphiLiveThresholdHitPrecision
{
    ExactFiveMinuteInterval,
    SessionOrdinal,
    NotReached,
    Unavailable
}

public enum DelphiLivePathOrdering
{
    ExactIntradayOrder,
    SameSessionUnknown,
    NotApplicable,
    Unavailable
}

public static class DelphiLiveOutcomeReasons
{
    public const string Valid = "Valid";
    public const string Pending = "Pending";
    public const string NotApplicable = "NotApplicable";
    public const string MissingExactEndpoint = "MissingExactEndpoint";
    public const string MissingContiguousPath = "MissingContiguousPath";
    public const string MissingMatchingXiu = "MissingMatchingXiu";
    public const string ConflictingEvidence = "ConflictingEvidence";
    public const string CorporateActionUnsupported = "CorporateActionUnsupported";
    public const string ThresholdReached = "ThresholdReached";
    public const string ThresholdNotReached = "ThresholdNotReached";
}

public readonly record struct DelphiLiveOutcomeMetric(
    DelphiLiveOutcomeMetricState State,
    decimal? Value,
    string ReasonCode)
{
    public static DelphiLiveOutcomeMetric Valid(decimal value) =>
        new(DelphiLiveOutcomeMetricState.Valid, value, DelphiLiveOutcomeReasons.Valid);

    public static DelphiLiveOutcomeMetric Invalid(string reasonCode) =>
        new(DelphiLiveOutcomeMetricState.Invalid, null, RequireReason(reasonCode));

    public static DelphiLiveOutcomeMetric Pending() =>
        new(DelphiLiveOutcomeMetricState.Pending, null, DelphiLiveOutcomeReasons.Pending);

    public static DelphiLiveOutcomeMetric NotApplicable() =>
        new(DelphiLiveOutcomeMetricState.NotApplicable, null, DelphiLiveOutcomeReasons.NotApplicable);

    public decimal RequireValue()
    {
        if (State is not (DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded) ||
            !Value.HasValue)
            throw new InvalidOperationException("The outcome metric has no usable value.");
        return Value.Value;
    }

    private static string RequireReason(string reasonCode) =>
        !string.IsNullOrWhiteSpace(reasonCode)
            ? reasonCode
            : throw new ArgumentException("A stable reason code is required.", nameof(reasonCode));
}

public sealed record DelphiLiveOpportunityThresholdHit(
    decimal Threshold,
    DelphiLiveOutcomeMetricState State,
    DelphiLiveThresholdHitPrecision Precision,
    DateTime? FirstIntervalEndUtc,
    int? FirstSessionOrdinal,
    string ReasonCode);

public sealed record DelphiLiveOutcomeHorizonResult(
    DelphiLiveOutcomeHorizon Horizon,
    DateTime? ExactEndpointUtc,
    DateOnly? ExactEndpointSession,
    DelphiLiveOutcomeMetric RawReturn,
    DelphiLiveOutcomeMetric XiuReturn,
    DelphiLiveOutcomeMetric ExcessReturn,
    DelphiLiveOutcomeMetric MaximumFavourableMovement,
    DelphiLiveOutcomeMetric MaximumAdverseMovement,
    DelphiLivePathOrdering PathOrdering,
    ImmutableArray<DelphiLiveOpportunityThresholdHit> OpportunityThresholds);

public sealed record DelphiLiveObservationOutcome(
    Guid OutcomeId,
    string OutcomeDefinition,
    Guid AnchorObservationId,
    Guid? XiuAnchorObservationId,
    string Symbol,
    DateOnly SessionDate,
    DateTime CheckpointEndUtc,
    DateTime AnchorReceivedUtc,
    decimal AnchorClose,
    decimal? XiuAnchorClose,
    DelphiLiveOutcomeEvidenceBasket EvidenceBasket,
    ImmutableArray<DelphiLiveOutcomeHorizonResult> Horizons);

public sealed record DelphiLiveOutcomeCalculationInput
{
    public required Guid OutcomeId { get; init; }
    public required DelphiLiveFiveMinuteBar Anchor { get; init; }
    public required DelphiLiveFiveMinuteBar? XiuAnchor { get; init; }
    public required DateTime SessionCloseUtc { get; init; }
    public required DateTime AsOfUtc { get; init; }
    public required DateOnly MaturedThroughSession { get; init; }
    public required IReadOnlyList<DateOnly> CanonicalSessionDates { get; init; }
    public required IReadOnlyList<DelphiLiveFiveMinuteBar> FutureIntradayBars { get; init; }
    public required IReadOnlyList<DelphiLiveFiveMinuteBar> FutureXiuIntradayBars { get; init; }
    public required IReadOnlyList<DelphiLiveDailyBar> FutureDailyBars { get; init; }
    public required IReadOnlyList<DelphiLiveDailyBar> FutureXiuDailyBars { get; init; }
    public required DelphiLiveOutcomeEvidenceBasket EvidenceBasket { get; init; }
    public bool CorporateActionUnsupported { get; init; }
}

/// <summary>
/// Calculates research-only forward labels from the close of an already known
/// canonical checkpoint. It never creates an executable price or repairs an
/// operational collection miss.
/// </summary>
public static class DelphiLiveObservationOutcomeCalculator
{
    private static readonly ImmutableArray<(DelphiLiveOutcomeHorizon Horizon, TimeSpan Span)>
        IntradayHorizons = ImmutableArray.Create(
            (DelphiLiveOutcomeHorizon.Minutes20, TimeSpan.FromMinutes(20)),
            (DelphiLiveOutcomeHorizon.Minutes60, TimeSpan.FromMinutes(60)),
            (DelphiLiveOutcomeHorizon.Minutes120, TimeSpan.FromMinutes(120)),
            (DelphiLiveOutcomeHorizon.Minutes180, TimeSpan.FromMinutes(180)));

    public static DelphiLiveObservationOutcome Calculate(
        DelphiLiveOutcomeCalculationInput input,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        Validate(input, policy);

        var results = ImmutableArray.CreateBuilder<DelphiLiveOutcomeHorizonResult>(7);
        foreach ((DelphiLiveOutcomeHorizon horizon, TimeSpan span) in IntradayHorizons)
            results.Add(CalculateIntraday(input, policy, horizon, input.Anchor.EndUtc + span));

        results.Add(CalculateSession1(input, policy));
        results.Add(CalculateLaterSession(input, policy, DelphiLiveOutcomeHorizon.Session3, 3));
        results.Add(CalculateLaterSession(input, policy, DelphiLiveOutcomeHorizon.Session5, 5));

        return new DelphiLiveObservationOutcome(
            input.OutcomeId,
            policy.ResearchOutcomeVersion,
            input.Anchor.ObservationId,
            input.XiuAnchor?.ObservationId,
            input.Anchor.Symbol,
            input.Anchor.SessionDate,
            input.Anchor.EndUtc,
            input.Anchor.ReceivedUtc,
            input.Anchor.Close,
            input.XiuAnchor?.Close,
            input.EvidenceBasket,
            results.MoveToImmutable());
    }

    private static DelphiLiveOutcomeHorizonResult CalculateIntraday(
        DelphiLiveOutcomeCalculationInput input,
        DelphiLivePolicyDefinition policy,
        DelphiLiveOutcomeHorizon horizon,
        DateTime endpointUtc)
    {
        if (endpointUtc > input.SessionCloseUtc)
            return Uniform(horizon, endpointUtc, input.Anchor.SessionDate, policy, DelphiLiveOutcomeMetric.NotApplicable());

        bool matured = input.AsOfUtc >= endpointUtc + policy.CollectionOffset;
        if (!matured)
            return Uniform(horizon, endpointUtc, input.Anchor.SessionDate, policy, DelphiLiveOutcomeMetric.Pending());
        if (input.CorporateActionUnsupported)
            return Uniform(
                horizon,
                endpointUtc,
                input.Anchor.SessionDate,
                policy,
                DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.CorporateActionUnsupported));

        CanonicalBarLookup stock = BuildIntradayLookup(
            input.FutureIntradayBars,
            input.Anchor.Symbol,
            input.Anchor.SessionDate,
            input.AsOfUtc);
        CanonicalBarLookup xiu = BuildIntradayLookup(
            input.FutureXiuIntradayBars,
            "XIU",
            input.Anchor.SessionDate,
            input.AsOfUtc);

        DelphiLiveOutcomeMetric raw = EndpointReturn(stock, endpointUtc, input.Anchor.Close);
        DelphiLiveOutcomeMetric xiuReturn = input.XiuAnchor is { } xiuAnchor
            ? EndpointReturn(xiu, endpointUtc, xiuAnchor.Close)
            : DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.MissingMatchingXiu);
        DelphiLiveOutcomeMetric excess = Excess(raw, xiuReturn);

        PathResult path = CalculateIntradayPath(
            stock,
            input.Anchor.EndUtc,
            endpointUtc,
            input.Anchor.Close,
            policy);

        return new DelphiLiveOutcomeHorizonResult(
            horizon,
            endpointUtc,
            input.Anchor.SessionDate,
            raw,
            xiuReturn,
            excess,
            path.Mfe,
            path.Mae,
            path.Ordering,
            path.Thresholds);
    }

    private static DelphiLiveOutcomeHorizonResult CalculateSession1(
        DelphiLiveOutcomeCalculationInput input,
        DelphiLivePolicyDefinition policy)
    {
        if (input.Anchor.EndUtc >= input.SessionCloseUtc)
            return Uniform(
                DelphiLiveOutcomeHorizon.Session1,
                input.SessionCloseUtc,
                input.Anchor.SessionDate,
                policy,
                DelphiLiveOutcomeMetric.NotApplicable());

        return CalculateIntraday(
            input,
            policy,
            DelphiLiveOutcomeHorizon.Session1,
            input.SessionCloseUtc);
    }

    private static DelphiLiveOutcomeHorizonResult CalculateLaterSession(
        DelphiLiveOutcomeCalculationInput input,
        DelphiLivePolicyDefinition policy,
        DelphiLiveOutcomeHorizon horizon,
        int targetOrdinal)
    {
        DateOnly? targetDate = ResolveTargetSession(input, targetOrdinal);
        if (!targetDate.HasValue || input.MaturedThroughSession < targetDate.Value)
            return Uniform(horizon, null, targetDate, policy, DelphiLiveOutcomeMetric.Pending());
        if (input.CorporateActionUnsupported)
            return Uniform(
                horizon,
                null,
                targetDate,
                policy,
                DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.CorporateActionUnsupported));

        DailyBarLookup stockDaily = BuildDailyLookup(input.FutureDailyBars, input.Anchor.Symbol);
        DailyBarLookup xiuDaily = BuildDailyLookup(input.FutureXiuDailyBars, "XIU");
        DelphiLiveOutcomeMetric raw = DailyEndpointReturn(stockDaily, targetDate.Value, input.Anchor.Close);
        DelphiLiveOutcomeMetric xiuReturn = input.XiuAnchor is { } xiuAnchor
            ? DailyEndpointReturn(xiuDaily, targetDate.Value, xiuAnchor.Close)
            : DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.MissingMatchingXiu);
        DelphiLiveOutcomeMetric excess = Excess(raw, xiuReturn);

        PathResult path = CalculateMultiSessionPath(
            input,
            policy,
            stockDaily,
            targetOrdinal);

        return new DelphiLiveOutcomeHorizonResult(
            horizon,
            null,
            targetDate,
            raw,
            xiuReturn,
            excess,
            path.Mfe,
            path.Mae,
            path.Ordering,
            path.Thresholds);
    }

    private static PathResult CalculateIntradayPath(
        CanonicalBarLookup lookup,
        DateTime anchorEndUtc,
        DateTime endpointUtc,
        decimal anchorClose,
        DelphiLivePolicyDefinition policy)
    {
        var path = new List<DelphiLiveFiveMinuteBar>();
        for (DateTime end = anchorEndUtc + policy.BarInterval;
             end <= endpointUtc;
             end += policy.BarInterval)
        {
            if (lookup.HasConflictAt(end))
                return InvalidPath(policy, DelphiLiveOutcomeReasons.ConflictingEvidence);
            if (!lookup.ByEnd.TryGetValue(end, out DelphiLiveFiveMinuteBar? bar))
                return InvalidPath(policy, DelphiLiveOutcomeReasons.MissingContiguousPath);
            path.Add(bar);
        }

        return CompletePath(path, Array.Empty<(int Ordinal, DelphiLiveDailyBar Bar)>(), anchorClose, policy);
    }

    private static PathResult CalculateMultiSessionPath(
        DelphiLiveOutcomeCalculationInput input,
        DelphiLivePolicyDefinition policy,
        DailyBarLookup dailyLookup,
        int targetOrdinal)
    {
        CanonicalBarLookup intraday = BuildIntradayLookup(
            input.FutureIntradayBars,
            input.Anchor.Symbol,
            input.Anchor.SessionDate,
            input.AsOfUtc);

        var remainingSession = new List<DelphiLiveFiveMinuteBar>();
        for (DateTime end = input.Anchor.EndUtc + policy.BarInterval;
             end <= input.SessionCloseUtc;
             end += policy.BarInterval)
        {
            if (intraday.HasConflictAt(end))
                return InvalidPath(policy, DelphiLiveOutcomeReasons.ConflictingEvidence);
            if (!intraday.ByEnd.TryGetValue(end, out DelphiLiveFiveMinuteBar? bar))
                return InvalidPath(policy, DelphiLiveOutcomeReasons.MissingContiguousPath);
            remainingSession.Add(bar);
        }

        int anchorIndex = FindAnchorSessionIndex(input);
        var laterSessions = new List<(int Ordinal, DelphiLiveDailyBar Bar)>();
        for (int ordinal = 2; ordinal <= targetOrdinal; ordinal++)
        {
            DateOnly date = input.CanonicalSessionDates[anchorIndex + ordinal - 1];
            if (dailyLookup.HasConflictAt(date))
                return InvalidPath(policy, DelphiLiveOutcomeReasons.ConflictingEvidence);
            if (!dailyLookup.ByDate.TryGetValue(date, out DelphiLiveDailyBar? bar))
                return InvalidPath(policy, DelphiLiveOutcomeReasons.MissingContiguousPath);
            laterSessions.Add((ordinal, bar));
        }

        return CompletePath(remainingSession, laterSessions, input.Anchor.Close, policy);
    }

    private static PathResult CompletePath(
        IReadOnlyList<DelphiLiveFiveMinuteBar> intraday,
        IReadOnlyList<(int Ordinal, DelphiLiveDailyBar Bar)> daily,
        decimal anchorClose,
        DelphiLivePolicyDefinition policy)
    {
        decimal maximum = 0m;
        decimal minimum = 0m;
        foreach (DelphiLiveFiveMinuteBar bar in intraday)
        {
            maximum = System.Math.Max(maximum, bar.High / anchorClose - 1m);
            minimum = System.Math.Min(minimum, bar.Low / anchorClose - 1m);
        }
        foreach ((int _, DelphiLiveDailyBar bar) in daily)
        {
            maximum = System.Math.Max(maximum, bar.High / anchorClose - 1m);
            minimum = System.Math.Min(minimum, bar.Low / anchorClose - 1m);
        }

        var hits = ImmutableArray.CreateBuilder<DelphiLiveOpportunityThresholdHit>(
            policy.OpportunityThresholds.Length);
        foreach (decimal threshold in policy.OpportunityThresholds)
        {
            DelphiLiveFiveMinuteBar? intradayHit = intraday.FirstOrDefault(
                bar => bar.High >= anchorClose * (1m + threshold));
            if (intradayHit is not null)
            {
                hits.Add(new DelphiLiveOpportunityThresholdHit(
                    threshold,
                    DelphiLiveOutcomeMetricState.Valid,
                    DelphiLiveThresholdHitPrecision.ExactFiveMinuteInterval,
                    intradayHit.EndUtc,
                    1,
                    DelphiLiveOutcomeReasons.ThresholdReached));
                continue;
            }

            (int Ordinal, DelphiLiveDailyBar Bar)? dailyHit = daily
                .Where(item => item.Bar.High >= anchorClose * (1m + threshold))
                .Select(item => ((int Ordinal, DelphiLiveDailyBar Bar)?)item)
                .FirstOrDefault();
            if (dailyHit.HasValue)
            {
                hits.Add(new DelphiLiveOpportunityThresholdHit(
                    threshold,
                    DelphiLiveOutcomeMetricState.Valid,
                    DelphiLiveThresholdHitPrecision.SessionOrdinal,
                    null,
                    dailyHit.Value.Ordinal,
                    DelphiLiveOutcomeReasons.ThresholdReached));
                continue;
            }

            hits.Add(new DelphiLiveOpportunityThresholdHit(
                threshold,
                DelphiLiveOutcomeMetricState.Valid,
                DelphiLiveThresholdHitPrecision.NotReached,
                null,
                null,
                DelphiLiveOutcomeReasons.ThresholdNotReached));
        }

        bool dailyOrderingUnknown = daily.Any(item =>
            item.Bar.High > anchorClose && item.Bar.Low < anchorClose);
        return new PathResult(
            DelphiLiveOutcomeMetric.Valid(maximum),
            DelphiLiveOutcomeMetric.Valid(minimum),
            dailyOrderingUnknown
                ? DelphiLivePathOrdering.SameSessionUnknown
                : DelphiLivePathOrdering.ExactIntradayOrder,
            hits.MoveToImmutable());
    }

    private static DelphiLiveOutcomeHorizonResult Uniform(
        DelphiLiveOutcomeHorizon horizon,
        DateTime? endpointUtc,
        DateOnly? endpointDate,
        DelphiLivePolicyDefinition policy,
        DelphiLiveOutcomeMetric metric)
    {
        DelphiLiveThresholdHitPrecision precision = metric.State switch
        {
            DelphiLiveOutcomeMetricState.NotApplicable => DelphiLiveThresholdHitPrecision.Unavailable,
            _ => DelphiLiveThresholdHitPrecision.Unavailable
        };
        ImmutableArray<DelphiLiveOpportunityThresholdHit> thresholds = policy.OpportunityThresholds
            .Select(threshold => new DelphiLiveOpportunityThresholdHit(
                threshold,
                metric.State,
                precision,
                null,
                null,
                metric.ReasonCode))
            .ToImmutableArray();
        return new DelphiLiveOutcomeHorizonResult(
            horizon,
            endpointUtc,
            endpointDate,
            metric,
            metric,
            metric,
            metric,
            metric,
            metric.State == DelphiLiveOutcomeMetricState.NotApplicable
                ? DelphiLivePathOrdering.NotApplicable
                : DelphiLivePathOrdering.Unavailable,
            thresholds);
    }

    private static PathResult InvalidPath(DelphiLivePolicyDefinition policy, string reason) =>
        new(
            DelphiLiveOutcomeMetric.Invalid(reason),
            DelphiLiveOutcomeMetric.Invalid(reason),
            DelphiLivePathOrdering.Unavailable,
            policy.OpportunityThresholds
                .Select(threshold => new DelphiLiveOpportunityThresholdHit(
                    threshold,
                    DelphiLiveOutcomeMetricState.Invalid,
                    DelphiLiveThresholdHitPrecision.Unavailable,
                    null,
                    null,
                    reason))
                .ToImmutableArray());

    private static DelphiLiveOutcomeMetric EndpointReturn(
        CanonicalBarLookup lookup,
        DateTime endpointUtc,
        decimal anchorClose)
    {
        if (lookup.HasConflictAt(endpointUtc))
            return DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.ConflictingEvidence);
        return lookup.ByEnd.TryGetValue(endpointUtc, out DelphiLiveFiveMinuteBar? bar)
            ? DelphiLiveOutcomeMetric.Valid(bar.Close / anchorClose - 1m)
            : DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.MissingExactEndpoint);
    }

    private static DelphiLiveOutcomeMetric DailyEndpointReturn(
        DailyBarLookup lookup,
        DateOnly endpointDate,
        decimal anchorClose)
    {
        if (lookup.HasConflictAt(endpointDate))
            return DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.ConflictingEvidence);
        return lookup.ByDate.TryGetValue(endpointDate, out DelphiLiveDailyBar? bar)
            ? DelphiLiveOutcomeMetric.Valid(bar.Close / anchorClose - 1m)
            : DelphiLiveOutcomeMetric.Invalid(DelphiLiveOutcomeReasons.MissingExactEndpoint);
    }

    private static DelphiLiveOutcomeMetric Excess(
        DelphiLiveOutcomeMetric raw,
        DelphiLiveOutcomeMetric xiu) =>
        raw.State is DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded &&
        xiu.State is DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded
            ? DelphiLiveOutcomeMetric.Valid(raw.RequireValue() - xiu.RequireValue())
            : DelphiLiveOutcomeMetric.Invalid(
                xiu.State is DelphiLiveOutcomeMetricState.Valid or DelphiLiveOutcomeMetricState.Degraded
                    ? raw.ReasonCode
                    : DelphiLiveOutcomeReasons.MissingMatchingXiu);

    private static DateOnly? ResolveTargetSession(
        DelphiLiveOutcomeCalculationInput input,
        int targetOrdinal)
    {
        int anchorIndex = FindAnchorSessionIndex(input);
        int targetIndex = anchorIndex + targetOrdinal - 1;
        return targetIndex < input.CanonicalSessionDates.Count
            ? input.CanonicalSessionDates[targetIndex]
            : null;
    }

    private static int FindAnchorSessionIndex(DelphiLiveOutcomeCalculationInput input)
    {
        for (int index = 0; index < input.CanonicalSessionDates.Count; index++)
        {
            if (input.CanonicalSessionDates[index] == input.Anchor.SessionDate)
                return index;
        }
        throw new ArgumentException("The canonical session path does not contain the anchor date.", nameof(input));
    }

    private static CanonicalBarLookup BuildIntradayLookup(
        IReadOnlyList<DelphiLiveFiveMinuteBar> bars,
        string symbol,
        DateOnly sessionDate,
        DateTime asOfUtc)
    {
        var lookup = new Dictionary<DateTime, DelphiLiveFiveMinuteBar>();
        var conflicts = new HashSet<DateTime>();
        foreach (DelphiLiveFiveMinuteBar bar in bars)
        {
            if (!string.Equals(bar.Symbol, symbol, StringComparison.Ordinal) ||
                bar.SessionDate != sessionDate || bar.ReceivedUtc > asOfUtc)
                continue;
            if (lookup.TryGetValue(bar.EndUtc, out DelphiLiveFiveMinuteBar? existing))
            {
                if (!Equivalent(existing, bar))
                    conflicts.Add(bar.EndUtc);
                continue;
            }
            lookup.Add(bar.EndUtc, bar);
        }
        return new CanonicalBarLookup(lookup, conflicts);
    }

    private static DailyBarLookup BuildDailyLookup(
        IReadOnlyList<DelphiLiveDailyBar> bars,
        string symbol)
    {
        var lookup = new Dictionary<DateOnly, DelphiLiveDailyBar>();
        var conflicts = new HashSet<DateOnly>();
        foreach (DelphiLiveDailyBar bar in bars)
        {
            if (!string.Equals(bar.Symbol, symbol, StringComparison.Ordinal))
                continue;
            if (lookup.TryGetValue(bar.SessionDate, out DelphiLiveDailyBar? existing))
            {
                if (existing.ObservationId != bar.ObservationId ||
                    existing.Open != bar.Open || existing.High != bar.High ||
                    existing.Low != bar.Low || existing.Close != bar.Close ||
                    existing.Volume != bar.Volume)
                    conflicts.Add(bar.SessionDate);
                continue;
            }
            lookup.Add(bar.SessionDate, bar);
        }
        return new DailyBarLookup(lookup, conflicts);
    }

    private static bool Equivalent(DelphiLiveFiveMinuteBar left, DelphiLiveFiveMinuteBar right) =>
        left.ObservationId == right.ObservationId &&
        left.Open == right.Open && left.High == right.High &&
        left.Low == right.Low && left.Close == right.Close &&
        left.Volume == right.Volume && left.ReceivedUtc == right.ReceivedUtc &&
        left.Disposition == right.Disposition;

    private static void Validate(
        DelphiLiveOutcomeCalculationInput input,
        DelphiLivePolicyDefinition policy)
    {
        if (input.OutcomeId == Guid.Empty)
            throw new ArgumentException("Outcome identity is required.", nameof(input));
        ArgumentNullException.ThrowIfNull(input.Anchor);
        DelphiLiveFiveMinuteBar.RequireUtc(input.SessionCloseUtc, nameof(input.SessionCloseUtc));
        DelphiLiveFiveMinuteBar.RequireUtc(input.AsOfUtc, nameof(input.AsOfUtc));
        if (string.Equals(input.Anchor.Symbol, "XIU", StringComparison.Ordinal))
            throw new ArgumentException("XIU is benchmark coverage and has no stock-style outcome.", nameof(input));
        if (input.XiuAnchor is { } xiuAnchor)
        {
            if (input.Anchor.SessionDate != xiuAnchor.SessionDate || input.Anchor.EndUtc != xiuAnchor.EndUtc)
                throw new ArgumentException("The stock and XIU anchors must use the same checkpoint.", nameof(input));
            if (!string.Equals(xiuAnchor.Symbol, "XIU", StringComparison.Ordinal))
                throw new ArgumentException("The benchmark anchor must be XIU.", nameof(input));
            if (input.AsOfUtc < xiuAnchor.ReceivedUtc)
                throw new ArgumentException("Outcome as-of time cannot precede the XIU anchor receipt.", nameof(input));
        }
        if (input.Anchor.EndUtc > input.SessionCloseUtc)
            throw new ArgumentException("The anchor cannot follow the regular-session close.", nameof(input));
        if (input.AsOfUtc < input.Anchor.ReceivedUtc)
            throw new ArgumentException("Outcome as-of time cannot precede the stock anchor receipt.", nameof(input));
        if (!Enum.IsDefined(input.EvidenceBasket))
            throw new ArgumentOutOfRangeException(nameof(input.EvidenceBasket));
        if (input.CanonicalSessionDates is null || input.FutureIntradayBars is null ||
            input.FutureXiuIntradayBars is null || input.FutureDailyBars is null ||
            input.FutureXiuDailyBars is null)
            throw new ArgumentException("Outcome evidence collections are required.", nameof(input));
        if (input.CanonicalSessionDates.Count == 0)
            throw new ArgumentException("Canonical session dates are required.", nameof(input));
        DateOnly prior = default;
        foreach (DateOnly date in input.CanonicalSessionDates)
        {
            if (prior != default && date <= prior)
                throw new ArgumentException("Canonical session dates must be unique and ascending.", nameof(input));
            prior = date;
        }
        _ = FindAnchorSessionIndex(input);
        if (policy.ResearchSessionHorizons.Length != 3 ||
            !policy.ResearchSessionHorizons.SequenceEqual(new[] { 1, 3, 5 }))
            throw new DelphiLivePolicyValidationException("V1 research session horizons must be 1, 3, and 5.");
    }

    private sealed record CanonicalBarLookup(
        IReadOnlyDictionary<DateTime, DelphiLiveFiveMinuteBar> ByEnd,
        IReadOnlySet<DateTime> Conflicts)
    {
        public bool HasConflict => Conflicts.Count > 0;
        public bool HasConflictAt(DateTime endpoint) => Conflicts.Contains(endpoint);
    }

    private sealed record DailyBarLookup(
        IReadOnlyDictionary<DateOnly, DelphiLiveDailyBar> ByDate,
        IReadOnlySet<DateOnly> Conflicts)
    {
        public bool HasConflict => Conflicts.Count > 0;
        public bool HasConflictAt(DateOnly date) => Conflicts.Contains(date);
    }

    private sealed record PathResult(
        DelphiLiveOutcomeMetric Mfe,
        DelphiLiveOutcomeMetric Mae,
        DelphiLivePathOrdering Ordering,
        ImmutableArray<DelphiLiveOpportunityThresholdHit> Thresholds);
}

public enum DelphiLiveCoverageReadiness
{
    NotMature,
    Ready,
    Degraded,
    Blocked,
    NotApplicable
}

public sealed record DelphiLiveMetricCoverage(
    int ValidCount,
    int DegradedCount,
    int InvalidCount,
    int PendingCount,
    int NotApplicableCount,
    int ApplicableCount,
    decimal? CompletionCoverage,
    decimal? UsableCoverage,
    DelphiLiveCoverageReadiness Readiness);

public static class DelphiLiveCoverageCalculator
{
    public static DelphiLiveMetricCoverage Calculate(
        IEnumerable<DelphiLiveOutcomeMetricState> states,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        DelphiLiveOutcomeMetricState[] values = states.ToArray();
        if (values.Any(value => !Enum.IsDefined(value)))
            throw new ArgumentOutOfRangeException(nameof(states));

        int valid = values.Count(value => value == DelphiLiveOutcomeMetricState.Valid);
        int degraded = values.Count(value => value == DelphiLiveOutcomeMetricState.Degraded);
        int invalid = values.Count(value => value == DelphiLiveOutcomeMetricState.Invalid);
        int pending = values.Count(value => value == DelphiLiveOutcomeMetricState.Pending);
        int notApplicable = values.Count(value => value == DelphiLiveOutcomeMetricState.NotApplicable);
        int applicable = values.Length - notApplicable;
        if (applicable == 0)
            return new DelphiLiveMetricCoverage(
                valid, degraded, invalid, pending, notApplicable, 0, null, null,
                DelphiLiveCoverageReadiness.NotApplicable);

        decimal completion = (decimal)(applicable - pending) / applicable;
        decimal usable = (decimal)(valid + degraded) / applicable;
        DelphiLiveCoverageReadiness readiness = pending > 0
            ? DelphiLiveCoverageReadiness.NotMature
            : usable == policy.ReadyCoverage
                ? DelphiLiveCoverageReadiness.Ready
                : usable >= policy.DegradedCoverageFloor
                    ? DelphiLiveCoverageReadiness.Degraded
                    : DelphiLiveCoverageReadiness.Blocked;
        return new DelphiLiveMetricCoverage(
            valid,
            degraded,
            invalid,
            pending,
            notApplicable,
            applicable,
            completion,
            usable,
            readiness);
    }
}
