#nullable enable

using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public interface IDelphiLiveClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemDelphiLiveClock : IDelphiLiveClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public interface ITsxSessionCalendar
{
    bool IsRegularSession(DateOnly tradingDate);
    DateOnly GetImmediatelyPrecedingSession(DateOnly tradingDate);
    DateOnly GetNextSession(DateOnly afterDate);
    int GetSessionOrdinal(DateOnly tradingDate);
    DelphiLiveSessionBounds GetSessionBounds(DateOnly tradingDate);
}

public sealed record DelphiLiveSessionBounds
{
    public DelphiLiveSessionBounds(
        DateOnly tradingDate,
        DateTime openUtc,
        DateTime closeUtc)
    {
        RequireUtc(openUtc, nameof(openUtc));
        RequireUtc(closeUtc, nameof(closeUtc));
        if (closeUtc <= openUtc)
            throw new ArgumentException("Session close must follow its open.");
        TradingDate = tradingDate;
        OpenUtc = openUtc;
        CloseUtc = closeUtc;
    }

    public DateOnly TradingDate { get; }
    public DateTime OpenUtc { get; }
    public DateTime CloseUtc { get; }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}

public interface ICanonicalXiuSessionSource
{
    Task<DateOnly?> GetImmediatelyPrecedingCompletedSessionAsync(
        DateOnly tradingDate,
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveMarketDataRequest(
    Guid CycleId,
    string Symbol,
    DateTime BarStartUtc,
    DateTime BarEndUtc,
    DateTime DeadlineUtc,
    DateTime RequestStartedUtc,
    int PriorityOrdinal);

public sealed record DelphiLiveMarketDataReceipt(
    DelphiLiveMarketDataRequest Request,
    OhlcvBar? ExactCompletedBar,
    DateTime ReceivedUtc,
    string Disposition,
    Guid? PollObservationId = null,
    Guid? EvidenceBarId = null)
{
    // Null explicitly means the source did not expose transport counts (for
    // example a client failure before a batch was returned), never zero work.
    public int? ProviderAttemptCount { get; init; }
    public int? ProviderRequestCount { get; init; }
    public DateTime? ProviderFetchStartedUtc { get; init; }
}

public sealed record DelphiLiveQuoteRequest(
    Guid DecisionId,
    string Symbol,
    string Side,
    int AttemptNumber,
    DateTime DecisionUtc,
    DateTime RequestStartedUtc);

public sealed record DelphiLiveQuoteReceipt(
    DelphiLiveQuoteRequest Request,
    decimal? Price,
    decimal? Bid,
    decimal? Ask,
    DateTime ReceivedUtc,
    string SourceContractVersion);

public interface IDelphiLiveMarketDataSource
{
    Task<DelphiLiveMarketDataReceipt> GetExactFiveMinuteBarAsync(
        DelphiLiveMarketDataRequest request,
        CancellationToken cancellationToken = default);

    Task<DelphiLiveQuoteReceipt> GetQuoteAsync(
        DelphiLiveQuoteRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDelphiLiveLeaseStore
{
    Task<DelphiLiveLease?> TryAcquireAsync(
        string ownerId,
        DateTime acquiredUtc,
        DateTime expiresUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryRenewAsync(
        DelphiLiveLease lease,
        DateTime renewedUtc,
        DateTime expiresUtc,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        DelphiLiveLease lease,
        DateTime releasedUtc,
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveLease(
    Guid LeaseId,
    string OwnerId,
    long FencingToken,
    DateTime AcquiredUtc,
    DateTime ExpiresUtc);

public interface IDelphiLivePolicyAssignmentSource
{
    Task<IReadOnlyList<DelphiLivePolicyAssignment>> GetAssignmentsForSessionAsync(
        DateOnly tradingDate,
        CancellationToken cancellationToken = default);
}

public enum DelphiLivePolicyRole
{
    OperationalChampion,
    ActiveShadowChallenger,
    ShadowBaseline
}

public sealed record DelphiLivePolicyAssignment(
    Guid AssignmentId,
    Guid PolicyVersionId,
    DelphiLivePolicyRole Role,
    DateOnly EffectiveSession,
    Guid? ExperimentId = null);

public interface IDelphiLiveHoldingSource
{
    Task<IReadOnlyList<DelphiLiveObservedHolding>> GetObservedHoldingsAsync(
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveObservedHolding(
    string Symbol,
    string Owner,
    Guid OwnerRecordId,
    bool DelphiLiveMayAct);

public interface IDelphiLiveNotifier
{
    Task NotifyAsync(
        DelphiLiveNotification notification,
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveNotification(
    string Severity,
    string Code,
    string Message,
    Guid? DecisionId = null,
    string? Symbol = null);

public interface IDelphiLiveSessionStore
{
    Task<DelphiLiveFrozenSession?> GetFrozenSessionAsync(
        DateOnly tradingDate,
        CancellationToken cancellationToken = default);

    Task<DelphiLiveFrozenSession> FreezeSessionAsync(
        DelphiLiveSessionFreezeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveSessionFreezeRequest(
    DateOnly TradingDate,
    DateTime FreezeBoundaryUtc,
    DateOnly ExpectedMarketDataAsOf,
    IReadOnlyList<DelphiLivePolicyAssignment> Assignments);

public sealed record DelphiLiveFrozenSession(
    Guid SessionId,
    DateOnly TradingDate,
    Guid? CalibrationRunId,
    Guid? DailyStrategyVersionId,
    string Status,
    DateTime FrozenUtc,
    IReadOnlyList<string> Symbols);

public interface IDelphiLiveCycleStore
{
    Task BeginCycleAsync(
        DelphiLiveCollectionCycle cycle,
        IReadOnlyList<DelphiLiveObservationTarget> expectedTargets,
        CancellationToken cancellationToken = default);

    Task<DelphiLiveMarketDataReceipt> RecordReceiptAsync(
        DelphiLiveMarketDataReceipt receipt,
        CancellationToken cancellationToken = default);

    Task CompleteCycleAsync(
        Guid cycleId,
        DateTime completedUtc,
        string status,
        CancellationToken cancellationToken = default);
}

public interface IDelphiLiveDecisionStore
{
    Task PersistEvaluationAsync(
        DelphiLivePersistedEvaluation evaluation,
        CancellationToken cancellationToken = default);

    Task PersistDecisionBeforeQuoteAsync(
        DelphiLivePersistedDecision decision,
        CancellationToken cancellationToken = default);

    Task PersistQuoteAttemptAsync(
        DelphiLiveQuoteReceipt quote,
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLivePersistedEvaluation(
    Guid EvaluationId,
    Guid SessionId,
    Guid PolicyVersionId,
    Guid DailyStrategyVersionId,
    string Symbol,
    DateTime BarEndUtc,
    string DossierJson);

public sealed record DelphiLivePersistedDecision(
    Guid DecisionId,
    Guid EvaluationId,
    string Action,
    string PrimaryReasonCode,
    DateTime DecidedUtc,
    string DossierJson);

public sealed record DelphiLiveCollectionCycle(
    Guid CycleId,
    Guid SessionId,
    DateTime BarStartUtc,
    DateTime BarEndUtc,
    DateTime ScheduledStartUtc,
    DateTime DeadlineUtc,
    long LeaseFencingToken,
    int ContinuityEpoch);

public enum DelphiLiveCollectionPriorityClass
{
    PendingProtectiveSell = 1,
    HeldSymbol = 2,
    XiuBenchmark = 3,
    ActiveCandidate = 4,
    QuietOrDismissedCandidate = 5
}

public sealed record DelphiLiveObservationTarget(
    string Symbol,
    DelphiLiveCollectionPriorityClass PriorityClass,
    int LiveOrder,
    bool IsCurrentSessionCandidate,
    bool IsSessionCarryCandidate);

public static class DelphiLiveCollectionPriorityPlanner
{
    public static IReadOnlyList<DelphiLiveObservationTarget> OrderAndDeduplicate(
        IEnumerable<DelphiLiveObservationTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var selected = new Dictionary<string, DelphiLiveObservationTarget>(
            StringComparer.OrdinalIgnoreCase);
        foreach (DelphiLiveObservationTarget target in targets)
        {
            ValidateTarget(target);
            if (!selected.TryGetValue(target.Symbol, out DelphiLiveObservationTarget? current) ||
                Compare(target, current) < 0)
            {
                selected[target.Symbol] = target;
            }
        }

        var ordered = new List<DelphiLiveObservationTarget>(selected.Values);
        ordered.Sort(Compare);
        return ordered.AsReadOnly();
    }

    private static int Compare(
        DelphiLiveObservationTarget left,
        DelphiLiveObservationTarget right)
    {
        int priority = left.PriorityClass.CompareTo(right.PriorityClass);
        if (priority != 0)
            return priority;
        int liveOrder = left.LiveOrder.CompareTo(right.LiveOrder);
        if (liveOrder != 0)
            return liveOrder;
        if (left.IsCurrentSessionCandidate != right.IsCurrentSessionCandidate)
            return left.IsCurrentSessionCandidate ? -1 : 1;
        if (left.IsSessionCarryCandidate != right.IsSessionCarryCandidate)
            return left.IsSessionCarryCandidate ? 1 : -1;
        return StringComparer.OrdinalIgnoreCase.Compare(left.Symbol, right.Symbol);
    }

    private static void ValidateTarget(DelphiLiveObservationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.Symbol) || target.Symbol.Length > 20)
            throw new ArgumentException("Target symbol is required and cannot exceed 20 characters.");
        if (!Enum.IsDefined(target.PriorityClass))
            throw new ArgumentOutOfRangeException(nameof(target));
        if (target.LiveOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(target));
    }
}

public static class DelphiLiveSchedule
{
    public static readonly TimeSpan RegularOpen = new(9, 30, 0);
    public static readonly TimeSpan FirstBarEnd = new(9, 35, 0);
    public static readonly TimeSpan FirstEntryBarEnd = new(9, 50, 0);
    public static readonly TimeSpan BuyCutoff = new(15, 45, 0);
    public static readonly TimeSpan RegularClose = new(16, 0, 0);
    public static readonly TimeSpan CollectionOffset = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan BarInterval = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<DateTime> GetBarEndsUtc(
        DateOnly tradingDate,
        TimeZoneInfo torontoTimeZone)
    {
        ArgumentNullException.ThrowIfNull(torontoTimeZone);
        var values = new List<DateTime>(78);
        for (TimeSpan end = FirstBarEnd; end <= RegularClose; end += BarInterval)
        {
            DateTime local = DateTime.SpecifyKind(
                tradingDate.ToDateTime(TimeOnly.FromTimeSpan(end)),
                DateTimeKind.Unspecified);
            values.Add(TimeZoneInfo.ConvertTimeToUtc(local, torontoTimeZone));
        }
        return values.AsReadOnly();
    }

    public static DateTime CollectionStartUtc(DateTime barEndUtc)
    {
        RequireUtc(barEndUtc, nameof(barEndUtc));
        return barEndUtc.Add(CollectionOffset);
    }

    public static DateTime CycleDeadlineUtc(DateTime barEndUtc)
    {
        RequireUtc(barEndUtc, nameof(barEndUtc));
        return CollectionStartUtc(barEndUtc).Add(BarInterval);
    }

    public static bool IsBuyDecisionBar(
        DateTime barEndUtc,
        TimeZoneInfo torontoTimeZone)
    {
        RequireUtc(barEndUtc, nameof(barEndUtc));
        ArgumentNullException.ThrowIfNull(torontoTimeZone);
        TimeSpan local = TimeZoneInfo.ConvertTimeFromUtc(barEndUtc, torontoTimeZone).TimeOfDay;
        return local >= FirstEntryBarEnd && local < BuyCutoff;
    }

    public static DateTime? NextCollectionStartUtc(
        DateOnly tradingDate,
        DateTime nowUtc,
        TimeZoneInfo torontoTimeZone)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        foreach (DateTime endUtc in GetBarEndsUtc(tradingDate, torontoTimeZone))
        {
            DateTime due = CollectionStartUtc(endUtc);
            if (due >= nowUtc)
                return due;
        }
        return null;
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}

public sealed record DelphiLiveContinuityState(
    int Epoch,
    int FreshConsecutiveObservationCount,
    DateTime? LastFreshBarEndUtc,
    bool BeganAtSessionOpen)
{
    public bool FourFamilyEvaluationMayBeMature =>
        FreshConsecutiveObservationCount >= (BeganAtSessionOpen ? 4 : 5);
}

public static class DelphiLiveContinuityPolicy
{
    public static DelphiLiveContinuityState Start(int epoch, bool beginsAtSessionOpen)
    {
        if (epoch < 1)
            throw new ArgumentOutOfRangeException(nameof(epoch));
        return new(epoch, 0, null, beginsAtSessionOpen);
    }

    public static DelphiLiveContinuityState Advance(
        DelphiLiveContinuityState current,
        DateTime barEndUtc,
        bool exactPairPersistedOnTime)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (barEndUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Bar end must be UTC.", nameof(barEndUtc));
        if (!exactPairPersistedOnTime)
            return Start(checked(current.Epoch + 1), false);

        bool consecutive = current.LastFreshBarEndUtc is null ||
            barEndUtc - current.LastFreshBarEndUtc.Value == DelphiLiveSchedule.BarInterval;
        if (!consecutive)
            return new(checked(current.Epoch + 1), 1, barEndUtc, false);

        return current with
        {
            FreshConsecutiveObservationCount = checked(current.FreshConsecutiveObservationCount + 1),
            LastFreshBarEndUtc = barEndUtc
        };
    }
}
