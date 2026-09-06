#nullable enable

using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public static class DelphiLiveCollectionDispositions
{
    public const string OperationalOnTime = "OperationalOnTime";
    public const string LateResearchOnly = "LateResearchOnly";
    public const string NoCompletedBar = "NoCompletedBar";
    public const string StaleNoNewBar = "StaleNoNewBar";
    public const string FormingBarIgnored = "FormingBarIgnored";
    public const string StructurallyInvalid = "StructurallyInvalid";
    public const string CycleDeadlineExceeded = "CycleDeadlineExceeded";
    public const string CollectionFailed = "CollectionFailed";
}

public sealed record DelphiLiveCollectionRunResult(
    Guid CycleId,
    string Status,
    int ExpectedTargets,
    int AttemptedTargets,
    int OperationalReceipts,
    int LateResearchReceipts,
    int MissedTargets,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Host-neutral, policy-neutral shared five-minute collector. It serializes work
/// by durable lease and persists the complete expected set before issuing the
/// first source request. Consumers evaluate the resulting canonical slots later.
/// </summary>
public sealed class DelphiLiveCollectionWorkflow
{
    private readonly IDelphiLiveClock clock;
    private readonly IDelphiLiveMarketDataSource marketData;
    private readonly IDelphiLiveCycleStore store;
    private readonly IDelphiLiveLeaseStore leases;
    private readonly SemaphoreSlim cycleGate = new(1, 1);

    public DelphiLiveCollectionWorkflow(
        IDelphiLiveClock clock,
        IDelphiLiveMarketDataSource marketData,
        IDelphiLiveCycleStore store,
        IDelphiLiveLeaseStore leases)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.leases = leases ?? throw new ArgumentNullException(nameof(leases));
    }

    public async Task<DelphiLiveCollectionRunResult> RunCycleAsync(
        DelphiLiveCollectionCycle cycle,
        IEnumerable<DelphiLiveObservationTarget> requestedTargets,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        ValidateCycle(cycle);
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 128)
            throw new ArgumentException("A bounded lease owner identity is required.", nameof(ownerId));
        IReadOnlyList<DelphiLiveObservationTarget> targets =
            DelphiLiveCollectionPriorityPlanner.OrderAndDeduplicate(requestedTargets);
        if (!targets.Any(x => x.Symbol.Equals("XIU", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A collection cycle requires at least the XIU target.", nameof(requestedTargets));

        DateTime acquireUtc = clock.UtcNow;
        RequireUtc(acquireUtc, nameof(clock.UtcNow));
        if (acquireUtc < cycle.ScheduledStartUtc || acquireUtc >= cycle.DeadlineUtc)
            throw new InvalidOperationException("Only the current scheduled collection cycle may run.");
        DelphiLiveLease? lease = await leases.TryAcquireAsync(
            ownerId,
            acquireUtc,
            cycle.DeadlineUtc,
            cancellationToken);
        if (lease is null)
        {
            return new(
                cycle.CycleId,
                "LeaseUnavailable",
                targets.Count,
                0,
                0,
                0,
                targets.Count,
                new[] { "Another durable Delphi Live host owns this cycle." });
        }
        try
        {
            return await RunCycleAsync(cycle, targets, lease, cancellationToken);
        }
        finally
        {
            await leases.ReleaseAsync(lease, clock.UtcNow, CancellationToken.None);
        }
    }

    // A running host owns one lease across cycles. Acquiring and releasing a
    // lease per poll would incorrectly turn every poll into a continuity gap.
    public async Task<DelphiLiveCollectionRunResult> RunCycleAsync(
        DelphiLiveCollectionCycle cycle,
        IEnumerable<DelphiLiveObservationTarget> requestedTargets,
        DelphiLiveLease hostLease,
        CancellationToken cancellationToken = default)
    {
        ValidateCycle(cycle);
        ArgumentNullException.ThrowIfNull(hostLease);
        var targets = DelphiLiveCollectionPriorityPlanner.OrderAndDeduplicate(requestedTargets);
        if (!targets.Any(x => x.Symbol.Equals("XIU", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A collection cycle requires the XIU target.", nameof(requestedTargets));
        if (cycle.LeaseFencingToken > 0 && cycle.LeaseFencingToken != hostLease.FencingToken)
            throw new InvalidOperationException("Cycle fencing token does not match the durable host lease.");
        if (clock.UtcNow < cycle.ScheduledStartUtc || clock.UtcNow >= cycle.DeadlineUtc)
            throw new InvalidOperationException("Only the current scheduled collection cycle may run.");
        if (!await cycleGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Delphi Live collection cycles cannot overlap.");
        try
        {
            if (!await leases.TryRenewAsync(hostLease, clock.UtcNow,
                    cycle.DeadlineUtc.AddMinutes(5), cancellationToken))
                throw new InvalidOperationException("Delphi Live host lease was lost.");
            return await CollectAsync(cycle with { LeaseFencingToken = hostLease.FencingToken },
                targets, cancellationToken);
        }
        finally
        {
            cycleGate.Release();
        }
    }

    private async Task<DelphiLiveCollectionRunResult> CollectAsync(
        DelphiLiveCollectionCycle cycle,
        IReadOnlyList<DelphiLiveObservationTarget> targets,
        CancellationToken cancellationToken)
    {
        int attempted = 0;
        int operational = 0;
        int late = 0;
        var warnings = new List<string>();
        string status = "Completed";
        try
        {
            await store.BeginCycleAsync(cycle, targets, cancellationToken);
            foreach (DelphiLiveObservationTarget target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTime requestUtc = clock.UtcNow;
                RequireUtc(requestUtc, nameof(clock.UtcNow));
                if (requestUtc >= cycle.DeadlineUtc)
                {
                    status = "DeadlineExceeded";
                    warnings.Add("Cycle deadline reached; remaining expected slots stay visible as misses.");
                    break;
                }

                attempted++;
                var request = new DelphiLiveMarketDataRequest(
                    cycle.CycleId,
                    target.Symbol,
                    cycle.BarStartUtc,
                    cycle.BarEndUtc,
                    cycle.DeadlineUtc,
                    requestUtc,
                    attempted);
                DelphiLiveMarketDataReceipt receipt;
                try
                {
                    using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadlineCancellation.CancelAfter(cycle.DeadlineUtc - requestUtc);
                    // Await even a provider that ignores cancellation: a late
                    // response is research-only and must not overlap a new poll.
                    receipt = await marketData.GetExactFiveMinuteBarAsync(request, deadlineCancellation.Token);
                    receipt = NormalizeReceipt(request, receipt, cycle.DeadlineUtc);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    receipt = new(
                        request,
                        null,
                        clock.UtcNow,
                        DelphiLiveCollectionDispositions.CycleDeadlineExceeded);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    warnings.Add($"{target.Symbol}: collection failed ({exception.GetType().Name}).");
                    receipt = new(
                        request,
                        null,
                        clock.UtcNow,
                        DelphiLiveCollectionDispositions.CollectionFailed);
                }

                receipt = await store.RecordReceiptAsync(receipt, cancellationToken);
                if (receipt.Disposition == DelphiLiveCollectionDispositions.OperationalOnTime)
                    operational++;
                else if (receipt.Disposition == DelphiLiveCollectionDispositions.LateResearchOnly)
                    late++;
            }
            if (clock.UtcNow >= cycle.DeadlineUtc)
                status = "DeadlineExceeded";
            await store.CompleteCycleAsync(cycle.CycleId, clock.UtcNow, status, cancellationToken);
        }
        catch
        {
            status = "Failed";
            try
            {
                await store.CompleteCycleAsync(cycle.CycleId, clock.UtcNow, status, CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure. An open durable cycle is itself a
                // visible incomplete-session fact for recovery and review.
            }
            throw;
        }

        int missed = targets.Count - operational;
        return new(
            cycle.CycleId,
            status,
            targets.Count,
            attempted,
            operational,
            late,
            missed,
            warnings.AsReadOnly());
    }

    internal static DelphiLiveMarketDataReceipt NormalizeReceipt(
        DelphiLiveMarketDataRequest expected,
        DelphiLiveMarketDataReceipt receipt,
        DateTime deadlineUtc)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(receipt);
        RequireUtc(deadlineUtc, nameof(deadlineUtc));
        if (receipt.Request != expected)
            throw new ArgumentException("Market-data receipt does not match its exact request.", nameof(receipt));
        RequireUtc(receipt.ReceivedUtc, nameof(receipt.ReceivedUtc));
        if (receipt.ReceivedUtc < expected.RequestStartedUtc)
            throw new ArgumentException("Market-data receipt precedes its request.", nameof(receipt));

        if (receipt.ExactCompletedBar is not OhlcvBar bar)
        {
            string disposition = receipt.Disposition switch
            {
                DelphiLiveCollectionDispositions.StaleNoNewBar or
                DelphiLiveCollectionDispositions.StructurallyInvalid or
                DelphiLiveCollectionDispositions.FormingBarIgnored or
                DelphiLiveCollectionDispositions.CollectionFailed or
                DelphiLiveCollectionDispositions.CycleDeadlineExceeded => receipt.Disposition,
                _ => DelphiLiveCollectionDispositions.NoCompletedBar
            };
            return receipt with { Disposition = disposition };
        }

        bool exact = bar.TimestampUtc.Kind == DateTimeKind.Utc && bar.TimestampUtc == expected.BarStartUtc;
        bool forming = bar.TimestampUtc.AddMinutes(5) >= receipt.ReceivedUtc;
        bool structurallyValid =
            bar.Open > 0m && bar.High > 0m && bar.Low > 0m && bar.Close > 0m &&
            bar.Low <= System.Math.Min(bar.Open, bar.Close) &&
            bar.High >= System.Math.Max(bar.Open, bar.Close) &&
            bar.Low <= bar.High && bar.Volume >= 0;
        string normalized = !exact
            ? bar.TimestampUtc < expected.BarStartUtc
                ? DelphiLiveCollectionDispositions.StaleNoNewBar
                : DelphiLiveCollectionDispositions.NoCompletedBar
            : forming
                ? DelphiLiveCollectionDispositions.FormingBarIgnored
                : !structurallyValid
                    ? DelphiLiveCollectionDispositions.StructurallyInvalid
                    : receipt.ReceivedUtc >= deadlineUtc
                        ? DelphiLiveCollectionDispositions.LateResearchOnly
                        : DelphiLiveCollectionDispositions.OperationalOnTime;
        return receipt with { Disposition = normalized };
    }

    private static void ValidateCycle(DelphiLiveCollectionCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        if (cycle.CycleId == Guid.Empty || cycle.SessionId == Guid.Empty)
            throw new ArgumentException("Cycle and session identities are required.", nameof(cycle));
        RequireUtc(cycle.BarStartUtc, nameof(cycle.BarStartUtc));
        RequireUtc(cycle.BarEndUtc, nameof(cycle.BarEndUtc));
        RequireUtc(cycle.ScheduledStartUtc, nameof(cycle.ScheduledStartUtc));
        RequireUtc(cycle.DeadlineUtc, nameof(cycle.DeadlineUtc));
        if (cycle.BarEndUtc - cycle.BarStartUtc != DelphiLiveSchedule.BarInterval ||
            cycle.ScheduledStartUtc != cycle.BarEndUtc + DelphiLiveSchedule.CollectionOffset ||
            cycle.DeadlineUtc != cycle.ScheduledStartUtc + DelphiLiveSchedule.BarInterval)
            throw new ArgumentException("Cycle timing does not match the frozen five-minute schedule.", nameof(cycle));
        if (cycle.LeaseFencingToken < 0 || cycle.ContinuityEpoch < 1)
            throw new ArgumentOutOfRangeException(nameof(cycle));
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
